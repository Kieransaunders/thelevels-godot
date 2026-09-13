using System;
using System.Numerics;
using NUnit.Framework;
using TheLevels.Core.Agents;
using TheLevels.Core.Simulation;

namespace TheLevels.Tests
{
    /// <summary>
    /// P8 wildlife gate, engine-free half: herd spawn and cohesion, grazing movement,
    /// fire panic, burn and drown deaths, frog margin spawn, hopping, diving under
    /// threat, and reset for both managers.
    /// </summary>
    public sealed class WildlifeTests
    {
        private HeightfieldSimulation heightfield = null!;
        private FireSimulation fire = null!;
        private HerdManager herd = null!;
        private FrogManager chorus = null!;

        [SetUp]
        public void SetUp()
        {
            heightfield = new HeightfieldSimulation { Paused = true };
            fire = new FireSimulation(heightfield) { Paused = true };
            herd = new HerdManager(heightfield, fire);
            chorus = new FrogManager(heightfield, fire);
        }

        [Test]
        public void HerdSpawnsTogetherOnDryGround()
        {
            Assert.That(herd.Total, Is.EqualTo(5));
            Assert.That(herd.AliveCount, Is.EqualTo(5));
            foreach (DeerAgent deer in herd.Agents)
            {
                Assert.That(heightfield.SampleWater(deer.Position), Is.LessThanOrEqualTo(0.02f), "deer in water");
                Assert.That(
                    Vector2.Distance(new Vector2(deer.Position.X, deer.Position.Z),
                        new Vector2(herd.SpawnCenter.X, herd.SpawnCenter.Z)),
                    Is.LessThanOrEqualTo(3.2f), "deer spawned away from the herd");
            }
        }

        [Test]
        public void DeerGrazeAndWanderWhileTheWorldIsPaused()
        {
            var before = new Vector2[herd.Total];
            for (int i = 0; i < herd.Total; i++)
                before[i] = new Vector2(herd.Agents[i].Position.X, herd.Agents[i].Position.Z);

            AdvanceHerd(12f);

            bool anyoneMoved = false;
            for (int i = 0; i < herd.Total; i++)
                if (Vector2.Distance(before[i], new Vector2(herd.Agents[i].Position.X, herd.Agents[i].Position.Z)) > 0.5f)
                    anyoneMoved = true;
            Assert.That(anyoneMoved, Is.True, "the herd never left its graze spots");
            Assert.That(herd.AliveCount, Is.EqualTo(5), "deer died on calm ground");
        }

        [Test]
        public void HerdBoltsFromFire()
        {
            // Alight inside the 12 m panic radius, clear of every body, where there is fuel.
            var threat = FindThreatPoint(new Vector2(herd.SpawnCenter.X, herd.SpawnCenter.Z),
                at => NearestDeerDistance(new Vector3(at.X, 0f, at.Y)) >= 5f);
            float nearest = NearestDeerDistance(threat);
            Assert.That(fire.Ignite(threat, 2.5f, 0.9f), Is.GreaterThan(0), "no fuel at the threat");

            AdvanceHerd(3f);

            Assert.That(herd.AliveCount, Is.EqualTo(5), "deer burned instead of fleeing");
            Assert.That(NearestDeerDistance(threat), Is.GreaterThan(nearest + 2f), "the herd did not bolt");
        }

        [Test]
        public void DeerBurnsWhenFlameReachesIt()
        {
            var victim = herd.Agents[0];
            Assert.That(fire.Ignite(victim.Position, 1.2f, 0.9f), Is.GreaterThan(0), "no fuel under the deer");

            AdvanceHerd(1f / 30f);

            Assert.That(victim.IsDead, Is.True);
            Assert.That(victim.Death, Is.EqualTo(DeerDeath.Burned));
        }

        [Test]
        public void FrogsSpawnOnTheWaterMargin()
        {
            Assert.That(chorus.Total, Is.EqualTo(12));
            foreach (FrogAgent frog in chorus.Frogs)
            {
                bool waterNear = heightfield.SampleWater(frog.Position) > 0.03f;
                foreach (Vector2 offset in new[] { new Vector2(2.5f, 0f), new Vector2(-2.5f, 0f),
                                                   new Vector2(0f, 2.5f), new Vector2(0f, -2.5f) })
                    if (heightfield.SampleWater(new Vector3(
                            frog.Position.X + offset.X, 0f, frog.Position.Z + offset.Y)) > 0.04f)
                        waterNear = true;
                Assert.That(waterNear, Is.True, $"frog at {frog.Position} has no water nearby");
            }
        }

        [Test]
        public void FrogsHopAsTimePasses()
        {
            int hops = 0;
            AdvanceChorus(10f);
            foreach (FrogAgent frog in chorus.Frogs) hops += frog.Hops;
            Assert.That(hops, Is.GreaterThan(0), "no frog ever hopped");
            Assert.That(chorus.AliveCount, Is.EqualTo(12), "frogs died on a calm bank");
        }

        [Test]
        public void FrogsDiveOrFleeFireAndSurviveIt()
        {
            // Ignite near the chorus (inside the 7 m threat radius, clear of bodies,
            // where there is fuel): the frogs dive or hop away, and none burns.
            var centroid = ChorusCentroid();
            float nearestFrog = float.MaxValue;
            var threat = FindThreatPoint(centroid, at =>
            {
                nearestFrog = float.MaxValue;
                foreach (FrogAgent frog in chorus.Frogs)
                    nearestFrog = MathF.Min(nearestFrog, Vector2.Distance(at,
                        new Vector2(frog.Position.X, frog.Position.Z)));
                return nearestFrog >= 2.5f;
            });
            Assert.That(fire.Ignite(threat, 2f, 0.9f), Is.GreaterThan(0), "no fuel at the threat");

            AdvanceChorus(2.5f);

            Assert.That(chorus.AliveCount, Is.EqualTo(12), "a frog burned near the threat");
            fire.ResetFire();
        }

        [Test]
        public void FrogBurnsWhenFlameReachesItsBank()
        {
            AdvanceChorus(0.2f); // let every frog settle into Sit
            FrogAgent victim = null!;
            foreach (FrogAgent frog in chorus.Frogs)
                if (frog.State is FrogState.Sit or FrogState.Hop) { victim = frog; break; }
            Assert.That(victim, Is.Not.Null, "no dry frog to burn");

            Assert.That(fire.Ignite(victim.Position, 1.2f, 0.9f), Is.GreaterThan(0), "no fuel under the frog");
            AdvanceChorus(1f / 30f);

            Assert.That(victim.IsDead, Is.True);
            Assert.That(victim.Death, Is.EqualTo(FrogDeath.Burned));
        }

        [Test]
        public void ResetRestoresHerdAndChorus()
        {
            fire.Ignite(herd.Agents[0].Position, 1.2f, 0.9f);
            AdvanceHerd(0.5f);
            Assert.That(herd.AliveCount, Is.LessThan(5), "setup: a deer should have burned");

            herd.ResetAll();
            chorus.ResetAll();

            Assert.That(herd.AliveCount, Is.EqualTo(5));
            Assert.That(chorus.AliveCount, Is.EqualTo(12));
            Assert.That(chorus.Total, Is.EqualTo(12));
        }

        /// <summary>
        /// A dry, fuelled point in expanding rings around <paramref name="anchor"/> that
        /// satisfies <paramref name="clear"/> — threat fires need real fuel to kindle.
        /// </summary>
        private Vector3 FindThreatPoint(Vector2 anchor, System.Func<Vector2, bool> clear)
        {
            for (int ring = 4; ring <= 9; ring++) // 1 m rings out to 9 m
            for (int step = 0; step < 8; step++)
            {
                float angle = step / 8f * MathF.PI * 2f;
                var at = anchor + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ring;
                var probe = new Vector3(at.X, 0f, at.Y);
                if (!heightfield.ContainsWorldPosition(probe)) continue;
                if (heightfield.SampleWater(probe) > 0.02f) continue;
                heightfield.WorldToGrid(probe, out float gx, out float gz);
                if (fire.GetFuel((int)gx, (int)gz) < 0.15f) continue;
                if (!clear(at)) continue;
                return probe;
            }
            Assert.Fail("no dry fuelled threat point near the anchor");
            return Vector3.Zero;
        }

        private void AdvanceHerd(float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f / 60f) herd.Advance(1f / 60f);
        }

        private void AdvanceChorus(float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f / 60f) chorus.Advance(1f / 60f);
        }

        private float NearestDeerDistance(Vector3 at)
        {
            float nearest = float.MaxValue;
            foreach (DeerAgent deer in herd.Agents)
                nearest = MathF.Min(nearest, Vector2.Distance(
                    new Vector2(deer.Position.X, deer.Position.Z), new Vector2(at.X, at.Z)));
            return nearest;
        }

        private Vector2 ChorusCentroid()
        {
            Vector2 sum = Vector2.Zero;
            foreach (FrogAgent frog in chorus.Frogs)
                sum += new Vector2(frog.Position.X, frog.Position.Z);
            return sum / chorus.Frogs.Count;
        }
    }
}
