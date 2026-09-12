using System.Numerics;
using NUnit.Framework;
using TheLevels.Core.Agents;
using TheLevels.Core.Simulation;

namespace TheLevels.Tests
{
    /// <summary>
    /// P5 exit gate, minus the two checks that need the engine (bridge passage and the
    /// visible haven): drowning, flame death, blocked passage, wading, panic recovery,
    /// survivor ritual and the all-dead case.
    /// </summary>
    public sealed class DruidTests
    {
        private HeightfieldSimulation heightfield = null!;
        private FireSimulation fire = null!;
        private DruidManager band = null!;

        [SetUp]
        public void SetUp()
        {
            heightfield = new HeightfieldSimulation { Paused = true };
            fire = new FireSimulation(heightfield) { Paused = true };
            band = new DruidManager(heightfield, fire);
        }

        [Test]
        public void BandSpawnsEightDruidsEastOfTheRhyne()
        {
            Assert.That(band.Total, Is.EqualTo(8));
            Assert.That(band.AliveCount, Is.EqualTo(8));
            Assert.That(band.RitualComplete, Is.False);
            foreach (DruidAgent agent in band.Agents)
                Assert.That(Horizontal(agent.Position, band.SpawnCenter), Is.EqualTo(2.6f).Within(0.01f));
        }

        [Test]
        public void RitualCompletesOnceWhenEverySurvivorIsInTheHaven()
        {
            int completions = 0;
            band.RitualCompleted += () => completions++;
            band.HavenCenter = band.SpawnCenter;  // spawn ring is 2.6 m, inside the 4.2 m haven
            band.ResetAll();

            band.Advance(1f / 60f);
            band.Advance(1f / 60f);

            Assert.That(band.RitualComplete, Is.True);
            Assert.That(completions, Is.EqualTo(1));
        }

        [Test]
        public void AllDeadDoesNotCompleteTheRitual()
        {
            Assert.That(fire.Ignite(band.SpawnCenter, 4.5f, 0.9f), Is.GreaterThan(0), "no fuel at the spawn");
            band.Advance(1f / 60f);

            Assert.That(band.AliveCount, Is.EqualTo(0));
            Assert.That(band.RitualComplete, Is.False, "an empty band of survivors is not a ritual");
            foreach (DruidAgent agent in band.Agents)
                Assert.That(agent.Death, Is.EqualTo(DruidDeath.Burned));
        }

        [Test]
        public void DeepWaterDrownsAfterTwoPointTwoSecondsAndBlocksTheStep()
        {
            // Deep past every 2.4 m probe, so the steering has nowhere safe to step.
            Flood(DryGround(), DruidAgent.DrownDepth + 0.2f, DruidAgent.ProbeDistance);
            DruidAgent agent = band.Agents[0];
            Vector2 start = new(agent.Position.X, agent.Position.Z);

            Advance(2.1f);
            Assert.That(agent.IsDead, Is.False, "drowned early");
            // Boxed in by deep water on every probe: the steering returns no safe step.
            Assert.That(Vector2.Distance(new Vector2(agent.Position.X, agent.Position.Z), start),
                Is.LessThan(0.01f), "walked out of deep water");

            Advance(0.3f);
            Assert.That(agent.IsDead, Is.True);
            Assert.That(agent.Death, Is.EqualTo(DruidDeath.Drowned));
        }

        [Test]
        public void ShallowWaterIsWadedAtReducedSpeed()
        {
            Flood(DryGround(), DruidAgent.WadeDepth + 0.1f, 0f);
            DruidAgent agent = band.Agents[0];
            Vector2 start = new(agent.Position.X, agent.Position.Z);

            band.Advance(1f / 60f);
            Assert.That(agent.Wading, Is.True);
            Advance(0.5f - 1f / 60f);

            Assert.That(agent.IsDead, Is.False);
            float travelled = Vector2.Distance(new Vector2(agent.Position.X, agent.Position.Z), start);
            Assert.That(travelled, Is.GreaterThan(0f), "stuck in the shallows");
            Assert.That(travelled, Is.LessThanOrEqualTo(DruidAgent.WalkSpeed * 0.45f * 0.5f + 0.01f));
        }

        [Test]
        public void NearbyFlamesCausePanicAndDousingRestoresTravel()
        {
            // Alight just outside the 2.4 m probe but inside the 8 m panic radius.
            var nearby = band.SpawnCenter + new Vector3(6f, 0f, 0f);
            Assert.That(fire.Ignite(nearby, 1.5f, 0.9f), Is.GreaterThan(0), "no fuel beside the spawn");
            band.Advance(1f / 60f);
            Assert.That(band.Agents[0].State, Is.EqualTo(DruidState.Panic));
            Assert.That(band.AliveCount, Is.EqualTo(8), "panic is not death");

            fire.ResetFire();
            Advance(DruidManager.ThreatScanInterval + 1f / 60f);

            Assert.That(band.Agents[0].State, Is.EqualTo(DruidState.Travel));
        }

        [Test]
        public void AnEarthBridgeOpensAChannelTheFloodHadClosed()
        {
            Vector3 channel = DryGround();
            Flood(channel, DruidAgent.DrownDepth + 0.2f, DruidAgent.ProbeDistance);
            DruidAgent agent = band.Agents[0];
            Vector2 start = new(agent.Position.X, agent.Position.Z);

            // Blocked: deep water past every probe, so the steering finds no safe step.
            Advance(0.5f);
            Assert.That(Horizontal(agent.Position, WithXZ(start)), Is.LessThan(0.01f), "walked through deep water");

            Bridge(channel);
            band.ResetAll();
            agent = band.Agents[0];
            float before = Horizontal(agent.Position, band.HavenCenter);
            Advance(1f);

            Assert.That(agent.IsDead, Is.False, "drowned on the bridge");
            Assert.That(Horizontal(agent.Position, band.HavenCenter), Is.LessThan(before - 0.5f),
                "did not cross the bridge toward the haven");
        }

        [Test]
        public void TurningTakesTheShortWayRoundThePiWrap()
        {
            // 3.0 to -3.0 is 0.283 rad the short way over PI, not 6.0 rad back through zero.
            // The result is not re-wrapped, so compare as an angle.
            Assert.That(Wrapped(DruidAgent.TurnToward(3.0f, -3.0f, 1f) - -3.0f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(DruidAgent.TurnToward(3.0f, -3.0f, 0.5f), Is.EqualTo(3.14159f).Within(0.01f),
                "turned the long way round");
            Assert.That(DruidAgent.TurnToward(0.2f, -0.2f, 1f), Is.EqualTo(-0.2f).Within(0.001f));
        }

        private static Vector3 WithXZ(Vector2 xz) => new(xz.X, 0f, xz.Y);

        private static float Wrapped(float radians)
        {
            float turn = System.MathF.PI * 2f;
            float wrapped = radians % turn;
            if (wrapped > System.MathF.PI) wrapped -= turn;
            if (wrapped < -System.MathF.PI) wrapped += turn;
            return wrapped;
        }

        /// <summary>Raises earth across the flooded channel and lets the water run off it.</summary>
        private void Bridge(Vector3 channel)
        {
            Vector3 quarry = DryGround();
            for (int load = 0; load < 60; load++)
            {
                heightfield.ApplyBrush(quarry, MatterType.Earth, true, 1f);
                heightfield.ApplyBrush(channel, MatterType.Earth, false, 1f);
            }
            heightfield.Paused = false;
            for (int step = 0; step < 240; step++) heightfield.StepOnce();
            heightfield.Paused = true;
            Assert.That(heightfield.SampleWater(channel), Is.LessThan(DruidAgent.WadeDepth),
                "the bridge did not clear the channel");
        }

        private void Advance(float seconds)
        {
            const float step = 1f / 60f;
            for (float t = 0f; t < seconds; t += step) band.Advance(step);
        }

        private static float Horizontal(Vector3 a, Vector3 b)
            => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

        /// <summary>The wettest interior cell, used as the water brush's source.</summary>
        private Vector3 WettestCell()
        {
            float half = heightfield.WorldSize * 0.5f;
            float best = 0f;
            Vector3 at = Vector3.Zero;
            for (int z = 4; z < heightfield.Resolution - 4; z++)
            for (int x = 4; x < heightfield.Resolution - 4; x++)
                if (heightfield.GetWater(x, z) > best)
                {
                    best = heightfield.GetWater(x, z);
                    at = new Vector3(x * heightfield.CellSize - half, 0f, z * heightfield.CellSize - half);
                }
            Assert.That(best, Is.GreaterThan(0f), "the test map has no water to scoop");
            return at;
        }

        /// <summary>An interior dry cell away from the rhyne, for the band to spawn on.</summary>
        private Vector3 DryGround()
        {
            float half = heightfield.WorldSize * 0.5f;
            for (int z = 12; z < heightfield.Resolution - 12; z++)
            for (int x = 12; x < heightfield.Resolution - 12; x++)
                if (heightfield.GetWater(x, z) <= 0f)
                    return new Vector3(x * heightfield.CellSize - half, 0f, z * heightfield.CellSize - half);
            Assert.Fail("no dry ground");
            return Vector3.Zero;
        }

        /// <summary>
        /// Pours water at <paramref name="center"/> until it is <paramref name="depth"/> deep
        /// there, and (when <paramref name="ring"/> is set) past the wade line all around at
        /// that radius. Spawns the band on the flooded spot.
        /// </summary>
        private void Flood(Vector3 center, float depth, float ring)
        {
            Vector3 source = WettestCell();
            for (int pour = 0; pour < 200 && !Flooded(center, depth, ring); pour++)
            {
                heightfield.ApplyBrush(source, MatterType.Water, true, 1f);
                heightfield.ApplyBrush(center, MatterType.Water, false, 1f);
            }
            Assert.That(Flooded(center, depth, ring), Is.True, "could not flood the spawn");
            // One druid, and the spawn ring's 2.6 m offset put on the flooded spot.
            band = new DruidManager(heightfield, fire, druidCount: 1)
            {
                SpawnCenter = center - new Vector3(2.6f, 0f, 0f)
            };
            band.ResetAll();
        }

        private bool Flooded(Vector3 center, float depth, float ring)
        {
            if (heightfield.SampleWater(center) < depth) return false;
            if (ring <= 0f) return true;
            foreach (Vector3 offset in new[]
                     {
                         new Vector3(ring, 0f, 0f), new Vector3(-ring, 0f, 0f),
                         new Vector3(0f, 0f, ring), new Vector3(0f, 0f, -ring)
                     })
                if (heightfield.SampleWater(center + offset) <= DruidAgent.WadeDepth + 0.05f) return false;
            return true;
        }
    }
}
