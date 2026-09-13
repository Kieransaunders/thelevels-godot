using System.Numerics;
using NUnit.Framework;
using TheLevels.Core.Simulation;
using TheLevels.Core.Vegetation;

namespace TheLevels.Tests
{
    /// <summary>
    /// P8 flora gate, engine-free half: deterministic grove scatter, fuel deposit,
    /// catching fire from the grid, grove-to-grove torch spread, charring, drowning,
    /// and reset — plus the FireSimulation.AddFuel contract the forest depends on.
    /// </summary>
    public sealed class ForestTests
    {
        private HeightfieldSimulation heightfield = null!;
        private FireSimulation fire = null!;
        private ForestManager forest = null!;

        [SetUp]
        public void SetUp()
        {
            heightfield = new HeightfieldSimulation { Paused = true };
            fire = new FireSimulation(heightfield) { Paused = true };
            forest = new ForestManager(heightfield, fire);
        }

        [Test]
        public void ScatterIsDeterministicOnDrySpacedGround()
        {
            var again = new ForestManager(heightfield, fire);
            Assert.That(again.Trees.Count, Is.EqualTo(forest.Trees.Count), "seeded scatter changed");
            for (int i = 0; i < forest.Trees.Count; i++)
            {
                Assert.That(forest.Trees[i].Position, Is.EqualTo(again.Trees[i].Position), $"tree {i} moved");
                Assert.That(forest.Trees[i].Species, Is.EqualTo(again.Trees[i].Species), $"tree {i} respeciesed");
            }

            Assert.That(forest.Trees.Count, Is.GreaterThanOrEqualTo(40), "the map grew no forest");
            for (int i = 0; i < forest.Trees.Count; i++)
            {
                Assert.That(heightfield.SampleWater(forest.Trees[i].Position), Is.LessThanOrEqualTo(0.02f),
                    $"tree {i} planted in water");
                for (int j = i + 1; j < forest.Trees.Count; j++)
                    Assert.That(
                        Vector2.Distance(
                            new Vector2(forest.Trees[i].Position.X, forest.Trees[i].Position.Z),
                            new Vector2(forest.Trees[j].Position.X, forest.Trees[j].Position.Z)),
                        Is.GreaterThanOrEqualTo(ForestManager.MinSpacing - 0.01f),
                        $"trees {i},{j} closer than the spacing rule");
            }
        }

        [Test]
        public void WoodFuelIsDepositedOnEveryTreeCell()
        {
            foreach (TreeAgent tree in forest.Trees)
            {
                heightfield.WorldToGrid(tree.Position, out float gx, out float gz);
                Assert.That(fire.GetFuel((int)gx, (int)gz), Is.GreaterThanOrEqualTo(ForestManager.FuelDeposit),
                    $"tree at {tree.Position} carries no wood fuel");
            }
        }

        [Test]
        public void AddFuelClampsToOneAndIgnoresOutOfBounds()
        {
            fire.AddFuel(10, 10, 0.5f);
            fire.AddFuel(10, 10, 0.5f);
            fire.AddFuel(10, 10, 0.5f);
            Assert.That(fire.GetFuel(10, 10), Is.EqualTo(1f).Within(0.0001f), "fuel must clamp to 1");
            Assert.DoesNotThrow(() =>
            {
                fire.AddFuel(-1, 0, 0.5f);
                fire.AddFuel(0, fire.Resolution, 0.5f);
                fire.AddFuel(fire.Resolution, -5, 1f);
            }, "out-of-bounds deposits must be ignored, not throw");
        }

        [Test]
        public void TreesCatchFireBurnCharAndTorchTheirGrove()
        {
            // Grove planting guarantees a neighbour inside the torch radius.
            TreeAgent torch = forest.Trees[0];
            int neighbours = 0;
            foreach (TreeAgent tree in forest.Trees)
                if (tree != torch && Vector2.Distance(
                        new Vector2(torch.Position.X, torch.Position.Z),
                        new Vector2(tree.Position.X, tree.Position.Z)) < TreeAgent.SpreadRadius)
                    neighbours++;
            Assert.That(neighbours, Is.GreaterThan(0), "first grove tree has no neighbour");

            Assert.That(fire.Ignite(torch.Position, 2.2f, 0.9f), Is.GreaterThan(0), "tree cell did not kindle");

            AdvanceForest(1.5f);
            Assert.That(torch.State, Is.EqualTo(TreeState.Burning), "tree did not catch from its burning cell");

            int peakBurning = 0;
            for (float t = 0f; t < TreeAgent.BurnSeconds + 0.5f; t += 1f / 30f)
            {
                forest.Advance(1f / 30f);
                fire.StepOnce();
                peakBurning = System.Math.Max(peakBurning, forest.BurningCount);
            }
            Assert.That(peakBurning, Is.GreaterThanOrEqualTo(2), "the fire never jumped to a neighbour");
            Assert.That(torch.State, Is.EqualTo(TreeState.Charred), "tree did not char after its burn");
        }

        [Test]
        public void FloodedTreeDrowns()
        {
            TreeAgent tree = forest.Trees[0];
            PourUntilDeep(tree.Position, TreeAgent.DrownDepth + 0.08f);

            AdvanceForest(TreeAgent.DrownAfterSeconds + 0.5f);

            Assert.That(tree.State, Is.EqualTo(TreeState.Drowned), "flooded tree did not drown");
            Assert.That(tree.Sink, Is.GreaterThan(0f), "drowned tree is not sinking");
        }

        [Test]
        public void GrowthClimbsFromSaplingToMature()
        {
            Assert.That(forest.Trees[0].Growth, Is.EqualTo(TreeAgent.SaplingGrowth).Within(0.001f));
            AdvanceForest(10f);
            Assert.That(forest.Trees[0].Growth, Is.GreaterThan(TreeAgent.SaplingGrowth), "sapling never grew");
            AdvanceForest(TreeAgent.GrowthSeconds);
            Assert.That(forest.Trees[0].Growth, Is.EqualTo(1f).Within(0.001f), "growth never capped");
        }

        [Test]
        public void ResetRestoresTheWholeForest()
        {
            fire.Ignite(forest.Trees[0].Position, 2.2f, 0.9f);
            AdvanceForest(TreeAgent.BurnSeconds + 1f);

            forest.ResetAll();

            Assert.That(forest.CharredCount + forest.BurningCount, Is.EqualTo(0), "reset left burned trees");
            Assert.That(forest.AliveCount, Is.EqualTo(forest.Trees.Count));
            foreach (TreeAgent tree in forest.Trees)
                Assert.That(tree.Growth, Is.EqualTo(TreeAgent.SaplingGrowth).Within(0.001f), "reset growth");
        }

        private void AdvanceForest(float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f / 30f) forest.Advance(1f / 30f);
        }

        /// <summary>Pours the solver's own water onto the tree while paused, so it pools.</summary>
        private void PourUntilDeep(Vector3 at, float depth)
        {
            Vector3 source = WettestCell();
            for (int pour = 0; pour < 400 && heightfield.SampleWater(at) < depth; pour++)
            {
                heightfield.ApplyBrush(source, MatterType.Water, true, 1f);
                heightfield.ApplyBrush(at, MatterType.Water, false, 1f);
            }
            Assert.That(heightfield.SampleWater(at), Is.GreaterThanOrEqualTo(depth), "could not flood the tree");
        }

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
    }
}
