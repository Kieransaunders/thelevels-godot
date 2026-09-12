using System;
using System.Numerics;
using TheLevels.Core.Agents;
using TheLevels.Core.Simulation;
using NUnit.Framework;

namespace TheLevels.Tests
{
    /// <summary>
    /// P7 integration hardening: 50-action transfer accounting, channel/bank/overtopping
    /// stability, and a 600-second mixed-tool soak. Engine-free like the rest of the
    /// suite; the adapter gates cover the engine boundary.
    /// Mass facts this suite relies on: brush edits move world mass by exactly the
    /// returned volume; earth scoops below the water table spring groundwater (measured
    /// per action as immediate water gain beyond the brush return); a solver step never
    /// *adds* water — it only loses it to the dry threshold (≤ DryEpsilon per drying
    /// cell) and to negative-value clamps.
    /// </summary>
    public sealed class IntegrationSoakTests
    {
        private HeightfieldSimulation simulation = null!;
        private FireSimulation fire = null!;
        private DruidManager band = null!;

        [SetUp]
        public void SetUp()
        {
            simulation = new HeightfieldSimulation();
            fire = new FireSimulation(simulation);
            band = new DruidManager(simulation, fire);
        }

        [Test]
        public void FiftyActionTransferScenarioKeepsExactVolumeAccounting()
        {
            double waterBefore = simulation.CalculateMetrics().WaterVolume;
            double earthBefore = simulation.CalculateMetrics().TerrainVolume;
            var rng = new Random(7701);

            for (int action = 0; action < 50; action++)
            {
                bool scoop = action % 2 == 0;
                var matter = action % 4 < 2 ? MatterType.Earth : MatterType.Water;
                var at = RandomPoint(rng, margin: 6f);
                float moved = simulation.ApplyBrush(at, matter, scoop, 0.25f);

                double waterNow = simulation.CalculateMetrics().WaterVolume;
                double earthNow = simulation.CalculateMetrics().TerrainVolume;

                // Brush edits are immediate: world mass must move by exactly the
                // returned volume (earth scoops may additionally spring groundwater).
                if (matter == MatterType.Earth)
                {
                    Assert.That(earthNow - earthBefore, Is.EqualTo(scoop ? -moved : moved).Within(0.01),
                        $"earth mass mismatch at action {action}");
                    if (scoop)
                        Assert.That(waterNow - waterBefore, Is.GreaterThanOrEqualTo(-0.01),
                            "earth scoop must never remove water");
                }
                else
                {
                    Assert.That(waterNow - waterBefore, Is.EqualTo(scoop ? -moved : moved).Within(0.01),
                        $"water mass mismatch at action {action}");
                }

                waterBefore = waterNow;
                earthBefore = earthNow;
            }

            Assert.That(simulation.LastError, Is.Null);

            // Steps never move earth, and only ever lose water.
            for (int i = 0; i < 100; i++) simulation.StepOnce();
            Assert.That(simulation.CalculateMetrics().TerrainVolume, Is.EqualTo(earthBefore).Within(0.5),
                "solver moved terrain");
            Assert.That(simulation.InvalidValueCount, Is.EqualTo(0));
        }

        [Test]
        public void ChannelBankAndOvertoppingScenarioStaysStable()
        {
            // Find deep water and a dry bank beside it; dig a channel out of the pool,
            // dam the channel with an earth bank, then step long enough to overtop.
            Vector3 pool = FindDeepestWater();
            Vector3 bankSpot = FindNearestDry(pool, minDistance: 6f);
            float bankBuilt = 0f;

            simulation.ApplyBrush(pool, MatterType.Water, false, 4f); // top up the pool
            for (int i = 0; i < 30; i++) simulation.StepOnce();

            // Channel: three scoops in a line away from the pool.
            Vector3 direction = Vector3.Normalize(new Vector3(bankSpot.X - pool.X, 0f, bankSpot.Z - pool.Z));
            for (int s = 1; s <= 3; s++)
                simulation.ApplyBrush(pool + direction * (4f * s), MatterType.Earth, true, 0.5f);
            for (int i = 0; i < 30; i++) simulation.StepOnce();

            // Bank: five drops across the channel mouth.
            Vector3 cross = new Vector3(-direction.Z, 0f, direction.X);
            for (int b = -2; b <= 2; b++)
            {
                float moved = simulation.ApplyBrush(bankSpot + cross * (1.4f * b), MatterType.Earth, false, 0.8f);
                bankBuilt += moved;
            }

            double waterBefore = simulation.CalculateMetrics().WaterVolume;
            for (int i = 0; i < 600; i++)
            {
                simulation.StepOnce();
                Assert.That(simulation.LastError, Is.Null, $"fault at step {i}");
            }
            double waterAfter = simulation.CalculateMetrics().WaterVolume;

            Assert.That(simulation.InvalidValueCount, Is.EqualTo(0));
            // Overtopping and seepage may move water around the domain, but the open
            // channel/bank scenario must not destroy it beyond dry-threshold losses.
            // Measured 1.736 m³ over 20 s: thin films at the flood front drying below
            // DryEpsilon (0.0005 m per cell per step across hundreds of front cells).
            Assert.That(waterBefore - waterAfter, Is.LessThan(2.5),
                $"channel scenario lost {waterBefore - waterAfter:0.000} m³ unaccounted");
            Assert.That(bankBuilt, Is.GreaterThan(0f), "bank build moved no earth");
        }

        [Test]
        public void TenMinuteSoakWithMixedToolsStaysHealthy()
        {
            const float soakSeconds = 600f;
            const float dt = 1f / 60f;
            var rng = new Random(7710);
            int actions = 0, resets = 0, strikes = 0, stepChecks = 0;
            float nextActionAt = 2f, nextStepCheckAt = 0.5f;
            double waterBeforeStep = simulation.CalculateMetrics().WaterVolume;
            // Per-segment reconciliation state; a segment ends at each reset.
            double segStartWater = simulation.CalculateMetrics().WaterVolume;
            double segBrush = 0d, segGround = 0d;
            double segLossAtStart = simulation.WaterLossVolume;
            float elapsed = 0f;

            while (elapsed < soakSeconds)
            {
                simulation.Advance(dt);
                fire.Advance(dt);
                band.Advance(dt);
                elapsed += dt;

                // Sampled: a solver step may only lose water (dry threshold, clamps),
                // never create it. Full reconciliation happens through the action log.
                if (elapsed >= nextStepCheckAt)
                {
                    nextStepCheckAt += 0.5f;
                    double after = simulation.CalculateMetrics().WaterVolume;
                    Assert.That(after, Is.LessThanOrEqualTo(waterBeforeStep + 0.0001),
                        $"solver created water at t={elapsed:0.0}s");
                    stepChecks++;
                    waterBeforeStep = after;
                }

                if (elapsed >= nextActionAt)
                {
                    nextActionAt += 2.5f;
                    int kind = rng.Next(6);
                    var at = RandomPoint(rng, margin: 10f);
                    double waterBeforeAction = simulation.CalculateMetrics().WaterVolume;
                    double terrainBeforeAction = simulation.CalculateMetrics().TerrainVolume;
                    switch (kind)
                    {
                        case 0:
                        {
                            float moved = simulation.ApplyBrush(at, MatterType.Earth, true, 0.5f);
                            segGround += Math.Max(0d,
                                simulation.CalculateMetrics().WaterVolume - waterBeforeAction);
                            Assert.That(simulation.CalculateMetrics().TerrainVolume,
                                Is.EqualTo(terrainBeforeAction - moved).Within(0.01), "earth scoop accounting");
                            break;
                        }
                        case 1:
                        {
                            float moved = simulation.ApplyBrush(at, MatterType.Earth, false, 0.5f);
                            Assert.That(simulation.CalculateMetrics().TerrainVolume,
                                Is.EqualTo(terrainBeforeAction + moved).Within(0.01), "earth drop accounting");
                            break;
                        }
                        case 2:
                            segBrush -= simulation.ApplyBrush(at, MatterType.Water, true, 0.5f);
                            break;
                        case 3:
                            segBrush += simulation.ApplyBrush(at, MatterType.Water, false, 0.5f);
                            break;
                        case 4:
                            fire.ApplyFireBrush(at, rng.Next(2) == 0, 0.3f);
                            break;
                        case 5:
                            fire.Strike(at, 2.8f);
                            strikes++;
                            break;
                    }
                    // Actions (brushes, groundwater seep) change water outside the step
                    // checks; refresh the baseline so sampled checks stay step-only.
                    waterBeforeStep = simulation.CalculateMetrics().WaterVolume;
                    actions++;
                    if (actions % 20 == 0)
                    {
                        ReconcileSegment(segStartWater, segBrush, segGround, segLossAtStart);
                        simulation.ResetSimulation();
                        fire.ResetFire();
                        band.ResetAll();
                        resets++;
                        segStartWater = simulation.CalculateMetrics().WaterVolume;
                        segBrush = 0d;
                        segGround = 0d;
                        segLossAtStart = simulation.WaterLossVolume;
                        waterBeforeStep = segStartWater;
                        nextStepCheckAt = elapsed + 0.5f;
                    }
                }
            }
            ReconcileSegment(segStartWater, segBrush, segGround, segLossAtStart);

            // Health: no self-paused fault, no invalid values, band structurally intact.
            Assert.That(simulation.LastError, Is.Null, "solver faulted during soak");
            Assert.That(simulation.InvalidValueCount, Is.EqualTo(0));
            Assert.That(actions, Is.GreaterThanOrEqualTo(200), "soak under-driven");
            Assert.That(resets, Is.GreaterThanOrEqualTo(2), "soak never reset");
            Assert.That(band.Total, Is.EqualTo(8), "band lost members structurally");

            for (int i = 0; i < simulation.Resolution * simulation.Resolution; i += 7)
            {
                float height = simulation.GetTerrain(i % simulation.Resolution, i / simulation.Resolution);
                Assert.That(height, Is.InRange(0.2f, 18f), $"terrain left the valid band at cell {i}");
            }

            TestContext.Progress.WriteLine(
                $"SOAK: {soakSeconds:0}s, {actions} actions, {resets} resets, {strikes} strikes, " +
                $"{stepChecks} step checks, {resets + 1} segments reconciled to ±0.5 m³, " +
                $"final solver-reported losses {simulation.WaterLossVolume:0.000} m³, " +
                $"final water {simulation.CalculateMetrics().WaterVolume:0.00} m³");
        }

        /// <summary>
        /// Every drop of water must be explained: start + brush inputs + groundwater
        /// seep − solver-reported losses (dry threshold, clamps) = current mass.
        /// </summary>
        private void ReconcileSegment(double segStartWater, double segBrush, double segGround,
            double segLossAtStart)
        {
            double waterNow = simulation.CalculateMetrics().WaterVolume;
            double lossNow = simulation.WaterLossVolume - segLossAtStart;
            double residual = segStartWater + segBrush + segGround - waterNow;
            Assert.That(Math.Abs(residual - lossNow), Is.LessThan(0.5),
                $"unexplained drift of {residual - lossNow:0.000} m³ " +
                $"(residual {residual:0.000}, solver losses {lossNow:0.000})");
        }

        private Vector3 RandomPoint(Random rng, float margin)
        {
            float half = simulation.WorldSize * 0.5f - margin;
            return new Vector3((float)(rng.NextDouble() * 2 - 1) * half, 0f,
                (float)(rng.NextDouble() * 2 - 1) * half);
        }

        private Vector3 FindDeepestWater()
        {
            float best = 0f;
            var at = new Vector3(0f, 0f, -1f);
            for (int z = 8; z < simulation.Resolution - 8; z++)
            for (int x = 8; x < simulation.Resolution - 8; x++)
            {
                if (simulation.GetWater(x, z) <= best) continue;
                best = simulation.GetWater(x, z);
                at = new Vector3(x * simulation.CellSize - simulation.WorldSize * 0.5f, 0f,
                    z * simulation.CellSize - simulation.WorldSize * 0.5f);
            }
            Assert.That(best, Is.GreaterThan(0.5f), "map has no deep pool for the channel scenario");
            return at;
        }

        private Vector3 FindNearestDry(Vector3 from, float minDistance)
        {
            for (int z = 8; z < simulation.Resolution - 8; z++)
            for (int x = 8; x < simulation.Resolution - 8; x++)
            {
                if (simulation.GetWater(x, z) > 0f) continue;
                var at = new Vector3(x * simulation.CellSize - simulation.WorldSize * 0.5f, 0f,
                    z * simulation.CellSize - simulation.WorldSize * 0.5f);
                if (Vector3.Distance(at, from) >= minDistance && simulation.SampleTerrain(at) < 8f)
                    return at;
            }
            Assert.Fail("no dry ground for the bank");
            return default;
        }
    }
}
