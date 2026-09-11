using System.Numerics;
using TheLevels.Core.Simulation;
using NUnit.Framework;

namespace TheLevels.Tests
{
    /// <summary>
    /// Port of the Unity prototype's FireSimulationTests (nine cases, 2026-09-07
    /// baseline). Assertions and tolerances are unchanged; Unity's GameObject setup
    /// became plain construction. Vector3.right/forward became Vector3.UnitX/UnitZ
    /// (same X/Z axes as Unity).
    /// </summary>
    public sealed class FireSimulationTests
    {
        private HeightfieldSimulation heightfield = null!;
        private FireSimulation fire = null!;

        [SetUp]
        public void SetUp()
        {
            heightfield = new HeightfieldSimulation();
            heightfield.Paused = true;
            fire = new FireSimulation(heightfield);
            fire.Paused = true;
        }

        [Test]
        public void InitialMapHasBurnableReedBeds()
        {
            float bestFuel = 0f;
            for (int z = 0; z < fire.Resolution; z++)
            for (int x = 0; x < fire.Resolution; x++)
                bestFuel = System.Math.Max(bestFuel, fire.GetFuel(x, z));

            Assert.That(bestFuel, Is.GreaterThan(0.3f), "The generated map must carry reed-bed fuel.");
        }

        [Test]
        public void LightningStrikeIgnitesDryReeds()
        {
            Vector3 dry = FindFuelledDryCell();
            int ignited = fire.Strike(dry, 3f);
            Assert.That(ignited, Is.GreaterThan(0));
            Assert.That(fire.BurningCells, Is.GreaterThan(0));
        }

        [Test]
        public void WetGroundShrugsOffLightning()
        {
            // Strike the deep pool interior; on a pool rim the disc overlaps dry reeds
            // and those legitimately catch.
            Vector3 deep = FindDeepWetCell();
            int ignited = fire.Strike(deep, 2f);
            Assert.That(ignited, Is.EqualTo(0), "Water and soaked ground must refuse to burn.");
            Assert.That(fire.BurningCells, Is.EqualTo(0));
        }

        [Test]
        public void FireSpreadsToNeighbouringCells()
        {
            Vector3 dry = FindFuelledDryCell();
            fire.Ignite(dry, 1.5f);
            for (int step = 0; step < 50; step++)
                fire.StepOnce();
            Assert.That(fire.BurningCells, Is.GreaterThanOrEqualTo(3), "Flames must cross cell borders.");
        }

        [Test]
        public void WaterDouseExtinguishesFire()
        {
            Vector3 dry = FindFuelledDryCell();
            fire.Ignite(dry, 1.5f);
            for (int step = 0; step < 6; step++)
                fire.StepOnce();
            Assert.That(fire.BurningCells, Is.GreaterThan(0));

            // Scoop from the pools, then dump across the whole burning patch — a single
            // brush-load over the origin leaves the racing front alive outside it.
            Vector3 wet = FindWetCell();
            Assert.That(heightfield.ApplyBrush(wet, MatterType.Water, true, 5f), Is.GreaterThan(0f));
            Vector3[] dousePoints =
            {
                dry,
                dry + Vector3.UnitX * 2.2f,
                dry - Vector3.UnitX * 2.2f,
                dry + Vector3.UnitZ * 2.2f,
                dry - Vector3.UnitZ * 2.2f
            };
            foreach (Vector3 at in dousePoints)
                heightfield.ApplyBrush(at, MatterType.Water, false, 1f);

            for (int step = 0; step < 10; step++)
                fire.StepOnce();
            Assert.That(fire.BurningCells, Is.EqualTo(0), "Standing water must kill the flames.");
        }

        [Test]
        public void FireBurnsOutAndCharresTheGround()
        {
            Vector3 dry = FindFuelledDryCell();
            fire.Ignite(dry, 2f);
            for (int step = 0; step < 300; step++)
                fire.StepOnce();

            heightfield.WorldToGrid(dry, out float gx, out float gz);
            int x = (int)gx, z = (int)gz;
            Assert.That(fire.GetFuel(x, z), Is.LessThan(0.05f), "Fuel is consumed.");
            Assert.That(fire.GetCharred(x, z), Is.True, "Burned-out ground stays charred.");
            Assert.That(fire.GetFire(x, z), Is.EqualTo(0f), "Nothing keeps burning without fuel.");
        }

        [Test]
        public void ScoopingFireFillsEmberBufferAndClearsFlames()
        {
            Vector3 dry = FindFuelledDryCell();
            fire.Ignite(dry, 1.5f);
            for (int step = 0; step < 6; step++)
                fire.StepOnce();
            Assert.That(fire.BurningCells, Is.GreaterThan(0));

            float scooped = fire.ApplyFireBrush(dry, true, 2f);
            Assert.That(scooped, Is.GreaterThan(0f));
            Assert.That(fire.EmberBuffer, Is.GreaterThan(0f));
            Assert.That(fire.BurningCells, Is.EqualTo(0), "The brush sweeps the flames clean.");
        }

        [Test]
        public void DroppingEmbersKindlesNewGround()
        {
            Vector3 first = FindFuelledDryCell();
            fire.Ignite(first, 1.5f);
            for (int step = 0; step < 6; step++)
                fire.StepOnce();
            fire.ApplyFireBrush(first, true, 2f);
            Assert.That(fire.EmberBuffer, Is.GreaterThan(0f));

            Vector3 second = FindFuelledDryCell(first, 18f);
            float bufferBefore = fire.EmberBuffer;
            float spent = fire.ApplyFireBrush(second, false, 2f);
            Assert.That(spent, Is.GreaterThan(0f));
            Assert.That(fire.EmberBuffer, Is.LessThan(bufferBefore));
            Assert.That(fire.BurningCells, Is.GreaterThan(0), "Embers dropped on dry reeds catch.");
        }

        [Test]
        public void ResetFireClearsFlamesAndEmbers()
        {
            Vector3 dry = FindFuelledDryCell();
            fire.Strike(dry, 3f);
            for (int step = 0; step < 10; step++)
                fire.StepOnce();

            fire.ResetFire();
            Assert.That(fire.BurningCells, Is.EqualTo(0));
            Assert.That(fire.CharredCells, Is.EqualTo(0));
            Assert.That(fire.EmberBuffer, Is.EqualTo(0f));
        }

        private Vector3 FindFuelledDryCell(Vector3? farFrom = null, float minDistance = 0f)
        {
            float half = heightfield.WorldSize * 0.5f;
            for (int z = 8; z < fire.Resolution - 8; z++)
            for (int x = 8; x < fire.Resolution - 8; x++)
            {
                if (fire.GetFuel(x, z) < 0.45f || heightfield.GetWater(x, z) > 0f)
                    continue;
                // Only cells whose whole neighbourhood burns, so spread tests are stable.
                if (fire.GetFuel(x + 1, z) < 0.2f || fire.GetFuel(x - 1, z) < 0.2f ||
                    fire.GetFuel(x, z + 1) < 0.2f || fire.GetFuel(x, z - 1) < 0.2f)
                    continue;

                Vector3 at = new(x * heightfield.CellSize - half, 0f, z * heightfield.CellSize - half);
                if (farFrom.HasValue && Vector3.Distance(at, farFrom.Value) < minDistance)
                    continue;
                return at;
            }

            Assert.Fail("No fuelled, dry, well-connected cell found on the generated map.");
            return default;
        }

        private Vector3 FindWetCell()
        {
            float half = heightfield.WorldSize * 0.5f;
            for (int z = 0; z < heightfield.Resolution; z++)
            for (int x = 0; x < heightfield.Resolution; x++)
            {
                if (heightfield.GetWater(x, z) > 0.05f)
                    return new Vector3(x * heightfield.CellSize - half, 0f, z * heightfield.CellSize - half);
            }

            Assert.Fail("The generated map did not contain a wet cell.");
            return default;
        }

        private Vector3 FindDeepWetCell()
        {
            int res = heightfield.Resolution;
            float half = heightfield.WorldSize * 0.5f;
            for (int z = 4; z < res - 4; z++)
            for (int x = 4; x < res - 4; x++)
            {
                if (heightfield.GetWater(x, z) < 1f)
                    continue;

                bool interior = true;
                for (int dz = -3; dz <= 3 && interior; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (heightfield.GetWater(x + dx, z + dz) < 0.1f)
                    {
                        interior = false;
                        break;
                    }
                }
                if (interior)
                    return new Vector3(x * heightfield.CellSize - half, 0f, z * heightfield.CellSize - half);
            }

            Assert.Fail("The map must contain a pool interior cell.");
            return default;
        }
    }
}
