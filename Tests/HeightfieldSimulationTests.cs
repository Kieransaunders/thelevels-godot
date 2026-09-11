using System.Numerics;
using TheLevels.Core.Simulation;
using NUnit.Framework;

namespace TheLevels.Tests
{
    /// <summary>
    /// Port of the Unity prototype's HeightfieldSimulationTests (five cases, 2026-09-07
    /// baseline). Assertions and tolerances are unchanged; only the Unity object setup
    /// (GameObject/AddComponent) became plain construction. Unity's Vector3 became
    /// System.Numerics.Vector3 with the same X/Z meaning.
    /// </summary>
    public sealed class HeightfieldSimulationTests
    {
        private HeightfieldSimulation simulation;

        [SetUp]
        public void SetUp()
        {
            simulation = new HeightfieldSimulation();
            simulation.Paused = true;
        }

        [Test]
        public void ResetRestoresInitialMatterAndClearsBuffers()
        {
            SimulationMetrics initial = simulation.CalculateMetrics();
            Vector3 scoopPoint = new Vector3(-8f, 0f, -4f);

            float scooped = simulation.ApplyBrush(scoopPoint, MatterType.Earth, true, 1f);
            Assert.That(scooped, Is.GreaterThan(0f));
            Assert.That(simulation.EarthBuffer, Is.GreaterThan(0f));

            simulation.ResetSimulation();
            SimulationMetrics reset = simulation.CalculateMetrics();

            Assert.That(simulation.EarthBuffer, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(simulation.WaterBuffer, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(reset.TerrainVolume, Is.EqualTo(initial.TerrainVolume).Within(0.001d));
            Assert.That(reset.WaterVolume, Is.EqualTo(initial.WaterVolume).Within(0.001d));
        }

        [Test]
        public void EarthScoopAndDropConserveWorldPlusBufferVolume()
        {
            SimulationMetrics initial = simulation.CalculateMetrics();
            double initialTotal = initial.TerrainVolume + simulation.EarthBuffer;

            float scooped = simulation.ApplyBrush(new Vector3(-8f, 0f, -4f), MatterType.Earth, true, 2f);
            float dropped = simulation.ApplyBrush(new Vector3(16f, 0f, -8f), MatterType.Earth, false, 2f);
            SimulationMetrics final = simulation.CalculateMetrics();
            double finalTotal = final.TerrainVolume + simulation.EarthBuffer;

            Assert.That(scooped, Is.GreaterThan(0f));
            Assert.That(dropped, Is.GreaterThan(0f));
            Assert.That(finalTotal, Is.EqualTo(initialTotal).Within(0.02d));
        }

        [Test]
        public void WaterPickupAndDropConserveWorldPlusBufferVolume()
        {
            Vector3 wetPoint = FindWetCell();
            SimulationMetrics initial = simulation.CalculateMetrics();
            double initialTotal = initial.WaterVolume + simulation.WaterBuffer;

            float scooped = simulation.ApplyBrush(wetPoint, MatterType.Water, true, 2f);
            float dropped = simulation.ApplyBrush(new Vector3(3f, 0f, 8f), MatterType.Water, false, 2f);
            SimulationMetrics final = simulation.CalculateMetrics();
            double finalTotal = final.WaterVolume + simulation.WaterBuffer;

            Assert.That(scooped, Is.GreaterThan(0f));
            Assert.That(dropped, Is.GreaterThan(0f));
            Assert.That(finalTotal, Is.EqualTo(initialTotal).Within(0.02d));
        }

        [Test]
        public void WaterSolverConservesVolumeAndNeverCreatesNegativeDepth()
        {
            double initialWater = simulation.CalculateMetrics().WaterVolume;

            for (int step = 0; step < 120; step++)
                simulation.StepOnce();

            double finalWater = simulation.CalculateMetrics().WaterVolume;
            Assert.That(finalWater, Is.EqualTo(initialWater).Within(initialWater * 0.02d));

            for (int z = 0; z < simulation.Resolution; z++)
            for (int x = 0; x < simulation.Resolution; x++)
                Assert.That(simulation.GetWater(x, z), Is.GreaterThanOrEqualTo(0f));
            Assert.That(simulation.InvalidValueCount, Is.EqualTo(0));
        }

        [Test]
        public void MomentumDevelopsThenSettlesOnPooledMap()
        {
            Assert.That(simulation.MaxWaterSpeed, Is.EqualTo(0f), "Starts at rest.");

            for (int step = 0; step < 20; step++)
                simulation.StepOnce();

            // The pools sit above the surrounding terrain, so their edges must accelerate:
            // the diffusive model could never produce a non-zero flow speed here.
            Assert.That(simulation.MaxWaterSpeed, Is.GreaterThan(0f), "Water gains momentum on slopes.");
            Assert.That(simulation.InvalidValueCount, Is.EqualTo(0));

            for (int step = 0; step < 600; step++)
                simulation.StepOnce();

            // With gravity draining toward equilibrium the flow must not diverge.
            Assert.That(simulation.MaxWaterSpeed, Is.LessThan(simulation.CellSize / simulation.FixedStep),
                "Speed stays within the CFL limit (no blow-up).");
            Assert.That(simulation.InvalidValueCount, Is.EqualTo(0));
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
