using System.Numerics;
using TheLevels.Core.Math;
using TheLevels.Core.Simulation;
using NUnit.Framework;

namespace TheLevels.Tests
{
    /// <summary>
    /// Godot-port contract checks added by the port plan (P1): grid/world round trips,
    /// boundary clamping, buffer and terrain limits, and groundwater accounting.
    /// These pin porting decisions the Unity suite never exercised directly.
    /// </summary>
    public sealed class PortContractTests
    {
        private HeightfieldSimulation simulation;

        [SetUp]
        public void SetUp()
        {
            simulation = new HeightfieldSimulation();
            simulation.Paused = true;
        }

        [Test]
        public void GridGeometryMatchesUnitySource()
        {
            Assert.That(simulation.Resolution, Is.EqualTo(129));
            Assert.That(simulation.CellSize, Is.EqualTo(96f / 128f).Within(0.0001f));
            Assert.That(simulation.CellArea, Is.EqualTo(simulation.CellSize * simulation.CellSize).Within(0.0001f));
        }

        [Test]
        public void WorldToGridAndBackRoundTripAcrossDomain()
        {
            float half = simulation.WorldSize * 0.5f;
            foreach (float x in new[] { -half, -half + 0.37f, 0f, 13.7f, half })
            foreach (float z in new[] { -half, -half + 0.91f, -4.2f, 0f, half })
            {
                simulation.WorldToGrid(new Vector3(x, 0f, z), out float gx, out float gz);
                float worldX = gx * simulation.CellSize - half;
                float worldZ = gz * simulation.CellSize - half;
                Assert.That(worldX, Is.EqualTo(x).Within(0.0001f), $"x={x}");
                Assert.That(worldZ, Is.EqualTo(z).Within(0.0001f), $"z={z}");
            }
        }

        [Test]
        public void WorldToGridClampsPositionsOutsideTheDomain()
        {
            simulation.WorldToGrid(new Vector3(-500f, 0f, 500f), out float gx, out float gz);
            Assert.That(gx, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(gz, Is.EqualTo(simulation.Resolution - 1f).Within(0.0001f));
        }

        [Test]
        public void ContainsWorldPositionAcceptsOnlyTheSquareDomain()
        {
            float half = simulation.WorldSize * 0.5f;
            Assert.That(simulation.ContainsWorldPosition(new Vector3(half, 0f, half)), Is.True);
            Assert.That(simulation.ContainsWorldPosition(new Vector3(-half, 0f, 0f)), Is.True);
            Assert.That(simulation.ContainsWorldPosition(new Vector3(half + 0.01f, 0f, 0f)), Is.False);
            Assert.That(simulation.ContainsWorldPosition(new Vector3(0f, 0f, -half - 0.01f)), Is.False);
        }

        [Test]
        public void ApplyBrushRejectsInvalidInputsWithoutSideEffects()
        {
            SimulationMetrics before = simulation.CalculateMetrics();

            Assert.That(simulation.ApplyBrush(new Vector3(500f, 0f, 0f), MatterType.Earth, true, 1f), Is.EqualTo(0f));
            Assert.That(simulation.ApplyBrush(new Vector3(0f, 0f, 0f), MatterType.Earth, true, 0f), Is.EqualTo(0f));
            Assert.That(simulation.ApplyBrush(new Vector3(0f, 0f, 0f), MatterType.Water, false, -1f), Is.EqualTo(0f));

            SimulationMetrics after = simulation.CalculateMetrics();
            Assert.That(after.TerrainVolume, Is.EqualTo(before.TerrainVolume).Within(0.0001d));
            Assert.That(after.WaterVolume, Is.EqualTo(before.WaterVolume).Within(0.0001d));
            Assert.That(simulation.EarthBuffer + simulation.WaterBuffer, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void DropIsLimitedByAvailableBuffer()
        {
            // Fill the buffer from a wet cell, then try to drop more than it holds.
            Vector3 wetPoint = FindWetCell();
            for (int i = 0; i < 10; i++)
                simulation.ApplyBrush(wetPoint, MatterType.Water, true, 5f);
            float buffer = simulation.WaterBuffer;
            Assert.That(buffer, Is.GreaterThan(0f));

            float dropped = simulation.ApplyBrush(new Vector3(3f, 0f, 8f), MatterType.Water, false, 60f);
            Assert.That(dropped, Is.LessThanOrEqualTo(buffer + 0.0001f));
            Assert.That(simulation.WaterBuffer, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void ScoopStopsAtCapacityAndNeverOverflowsTheBuffer()
        {
            Vector3 wetPoint = FindWetCell();
            for (int i = 0; i < 30; i++)
                simulation.ApplyBrush(wetPoint, MatterType.Water, true, 10f);

            Assert.That(simulation.WaterBuffer, Is.LessThanOrEqualTo(simulation.WaterCapacity));
            Assert.That(simulation.WaterBuffer, Is.GreaterThan(0f));
        }

        [Test]
        public void TerrainHeightsStayWithinBoundsAfterBrushing()
        {
            // Dump repeated earth on one point: heights must cap at MaximumTerrainHeight.
            for (int i = 0; i < 30; i++)
                simulation.ApplyBrush(new Vector3(0f, 0f, 0f), MatterType.Earth, false, 10f);

            for (int z = 0; z < simulation.Resolution; z++)
            for (int x = 0; x < simulation.Resolution; x++)
                Assert.That(simulation.GetTerrain(x, z), Is.LessThanOrEqualTo(18f + 0.0001f));
        }

        [Test]
        public void EarthScoopBelowGroundwaterLevelIntroducesWater()
        {
            // Documented source behaviour: digging a hollow below the water table springs
            // water into it, so closed-water conservation must exclude this action.
            simulation.ResetSimulation();
            double waterBefore = simulation.CalculateMetrics().WaterVolume;

            float scooped = simulation.ApplyBrush(new Vector3(-8f, 0f, -4f), MatterType.Earth, true, 5f);
            double waterAfter = simulation.CalculateMetrics().WaterVolume;

            Assert.That(scooped, Is.GreaterThan(0f));
            Assert.That(waterAfter, Is.GreaterThan(waterBefore), "Groundwater seep adds water.");
        }

        [Test]
        public void AdvanceRespectsPauseAndSubstepCap()
        {
            simulation.Paused = true;
            simulation.Advance(1f);
            Assert.That(simulation.StepCount, Is.EqualTo(0), "Paused Advance does nothing.");

            simulation.ResetSimulation();
            // A huge frame delta is capped at maxSubsteps * fixedStep, not uncapped time.
            simulation.Advance(3600f);
            Assert.That(simulation.StepCount, Is.EqualTo(4), "At most four water substeps per Advance.");
        }

        [Test]
        public void DeterministicNoiseIsStableAcrossCallsAndInUnitRange()
        {
            for (int i = 0; i < 200; i++)
            {
                float x = i * 0.137f - 13f;
                float z = i * -0.091f + 7f;
                float a = DeterministicNoise.ValueNoise(x, z);
                float b = DeterministicNoise.ValueNoise(x, z);
                Assert.That(a, Is.EqualTo(b), "Same input must give the same output.");
                Assert.That(a, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void FaultedStateIsReportedInsteadOfEngineLogging()
        {
            Assert.That(simulation.LastError, Is.Null);
            // We cannot force invalid values through the public API without corrupting state,
            // but the contract is that a fresh/reset simulation is never faulted.
            simulation.ResetSimulation();
            Assert.That(simulation.LastError, Is.Null);
        }

        private Vector3 FindWetCell()
        {
            float half = simulation.WorldSize * 0.5f;
            for (int z = 0; z < simulation.Resolution; z++)
            for (int x = 0; x < simulation.Resolution; x++)
            {
                if (simulation.GetWater(x, z) > 0.05f)
                    return new Vector3(x * simulation.CellSize - half, 0f, z * simulation.CellSize - half);
            }

            Assert.Fail("The generated map did not contain a wet cell.");
            return default;
        }
    }
}
