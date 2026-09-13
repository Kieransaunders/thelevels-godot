using System;
using System.Numerics;
using NUnit.Framework;
using TheLevels.Core.Agents;
using TheLevels.Core.Simulation;

namespace TheLevels.Tests
{
    /// <summary>
    /// Villager core behaviour: band composition, strolling inside the home radius,
    /// panic and refuge re-homing, deaths by the shared pedestrian rules, and parity
    /// of the steering the druids and villagers now share.
    /// </summary>
    public sealed class VillagerTests
    {
        private HeightfieldSimulation heightfield = null!;
        private FireSimulation fire = null!;
        private VillagerManager village = null!;

        [SetUp]
        public void SetUp()
        {
            heightfield = new HeightfieldSimulation { Paused = true };
            fire = new FireSimulation(heightfield) { Paused = true };
            village = new VillagerManager(heightfield, fire);
        }

        [Test]
        public void SettlementSpawnsFiveMenFourWomenThreeMothers()
        {
            Assert.That(village.Total, Is.EqualTo(12));
            Assert.That(village.AliveCount, Is.EqualTo(12));
            Assert.That(village.CountOf(VillagerKind.Man), Is.EqualTo(5));
            Assert.That(village.CountOf(VillagerKind.Woman), Is.EqualTo(4));
            Assert.That(village.CountOf(VillagerKind.Mother), Is.EqualTo(3));
            foreach (VillagerAgent villager in village.Agents)
            {
                Assert.That(Horizontal(villager.Position, village.HomeCenter), Is.EqualTo(3.2f).Within(0.01f));
                Assert.That(villager.State, Is.EqualTo(VillagerState.Stroll));
                Assert.That(villager.IsDead, Is.False);
            }
        }

        [Test]
        public void VillagersStrollButStayInsideTheHomeRadius()
        {
            var spawn = new Vector3[village.Total];
            for (int i = 0; i < village.Total; i++) spawn[i] = village.Agents[i].Position;

            Advance(10f);

            Assert.That(village.AliveCount, Is.EqualTo(12), "a calm settlement must not lose villagers");
            float furthest = 0f;
            foreach (VillagerAgent villager in village.Agents)
            {
                Assert.That(Horizontal(villager.Position, village.HomeCenter),
                    Is.LessThan(village.HomeRadius), "strolled out of the settlement");
                furthest = MathF.Max(furthest, Horizontal(villager.Position, spawn[IndexOf(villager)]));
            }
            Assert.That(furthest, Is.GreaterThan(0.5f), "nobody ever strolled");
        }

        [Test]
        public void FireBesideTheSettlementPanicsButKillsNoOne()
        {
            var nearby = village.HomeCenter + new Vector3(6f, 0f, 0f);
            Assert.That(fire.Ignite(nearby, 2f, 0.9f), Is.GreaterThan(0), "no fuel beside the settlement");

            village.Advance(1f / 60f);
            Assert.That(AnyPanic(), Is.True, "fire beside the settlement caused no panic");
            Assert.That(village.AliveCount, Is.EqualTo(12), "panic is not death");

            fire.ResetFire();
            Advance(VillagerManager.ThreatScanInterval + 2f);
            Assert.That(AnyPanic(), Is.False, "panic outlasted the fire");
        }

        [Test]
        public void PanicFlightCarriesMothersClearOfTheFire()
        {
            var threat = village.HomeCenter + new Vector3(6f, 0f, 0f);
            Assert.That(fire.Ignite(threat, 2f, 0.9f), Is.GreaterThan(0), "no fuel beside the settlement");

            Advance(4.5f);

            foreach (VillagerAgent villager in village.Agents)
            {
                Assert.That(villager.IsDead, Is.False, "a villager died fleeing a grass fire");
                if (villager.Kind != VillagerKind.Mother) continue;
                Assert.That(Horizontal(villager.Position, threat),
                    Is.GreaterThan(VillagerManager.PanicRadius), "a mother never cleared the fire");
            }
        }

        [Test]
        public void FleeingABurningSettlementReHomesAtTheRefuge()
        {
            // 5.5 m east puts the whole 3.2 m spawn ring (1.8–8.7 m) inside the 9 m panic
            // radius, while staying outside the 2 m ignite disc so nobody kindles where
            // they stand. 6 m would leave the ring's far villager at 9.2 m, calmly at home.
            var threat = village.HomeCenter + new Vector3(5.5f, 0f, 0f);
            Assert.That(fire.Ignite(threat, 2f, 0.9f), Is.GreaterThan(0), "no fuel beside the settlement");

        Advance(4.5f);

        foreach (VillagerAgent villager in village.Agents)
            Assert.That(Horizontal(villager.Home, threat), Is.GreaterThan(VillagerManager.PanicRadius),
                "a villager re-homed inside the burning settlement");
        }

        [Test]
        public void FireOnTheSettlementBurnsTheBand()
        {
            Assert.That(fire.Ignite(village.HomeCenter, 4.5f, 0.9f), Is.GreaterThan(0),
                "no fuel under the settlement");
            village.Advance(1f / 60f);

            Assert.That(village.AliveCount, Is.EqualTo(0));
            foreach (VillagerAgent villager in village.Agents)
                Assert.That(villager.Death, Is.EqualTo(VillagerDeath.Burned));
        }

        [Test]
        public void DeepWaterDrownsABoxedInVillagerAfterTwoPointTwoSeconds()
        {
            Flood(DryGround());
            VillagerAgent villager = village.Agents[0];
            Vector2 start = new(villager.Position.X, villager.Position.Z);

            Advance(2.1f);
            Assert.That(villager.IsDead, Is.False, "drowned early");
            Assert.That(Vector2.Distance(new Vector2(villager.Position.X, villager.Position.Z), start),
                Is.LessThan(0.01f), "walked out of deep water");

            Advance(0.3f);
            Assert.That(villager.IsDead, Is.True);
            Assert.That(villager.Death, Is.EqualTo(VillagerDeath.Drowned));
        }

        [Test]
        public void VillagersShareTheDruidsSteeringExactly()
        {
            // The druid wrap test pins the same function; this pins that the shared move
            // kept both delegates identical.
            foreach ((float from, float to) in new[] { (3.0f, -3.0f), (0.2f, -0.2f), (-2.6f, 2.6f) })
                Assert.That(DruidAgent.TurnToward(from, to, 0.5f),
                    Is.EqualTo(PedestrianSteering.TurnToward(from, to, 0.5f)));
        }

        private bool AnyPanic()
        {
            foreach (VillagerAgent villager in village.Agents)
                if (villager.State == VillagerState.Panic) return true;
            return false;
        }

        private int IndexOf(VillagerAgent villager)
        {
            for (int i = 0; i < village.Total; i++)
                if (ReferenceEquals(village.Agents[i], villager)) return i;
            return -1;
        }

        private void Advance(float seconds)
        {
            const float step = 1f / 60f;
            for (float t = 0f; t < seconds; t += step) village.Advance(step);
        }

        private static float Horizontal(Vector3 a, Vector3 b)
            => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

        /// <summary>
        /// Pours water at a dry interior spot until it is drown-deep there and past the
        /// wade line at the 2.4 m probe ring, then spawns a one-villager settlement on it.
        /// </summary>
        private void Flood(Vector3 center)
        {
            Vector3 source = WettestCell();
            for (int pour = 0; pour < 200 && !Flooded(center); pour++)
            {
                heightfield.ApplyBrush(source, MatterType.Water, true, 1f);
                heightfield.ApplyBrush(center, MatterType.Water, false, 1f);
            }
            Assert.That(Flooded(center), Is.True, "could not flood the spawn");
            // The single villager spawns at ring angle 0, i.e. HomeCenter + (3.2, 0, 0).
            village = new VillagerManager(heightfield, fire, villagerCount: 1)
            {
                HomeCenter = center - new Vector3(3.2f, 0f, 0f)
            };
            village.ResetAll();
        }

        private bool Flooded(Vector3 center)
        {
            if (heightfield.SampleWater(center) < PedestrianSteering.DrownDepth + 0.2f) return false;
            foreach (Vector3 offset in new[]
                     {
                         new Vector3(PedestrianSteering.ProbeDistance, 0f, 0f),
                         new Vector3(-PedestrianSteering.ProbeDistance, 0f, 0f),
                         new Vector3(0f, 0f, PedestrianSteering.ProbeDistance),
                         new Vector3(0f, 0f, -PedestrianSteering.ProbeDistance)
                     })
                if (heightfield.SampleWater(center + offset) <= PedestrianSteering.WadeDepth + 0.05f) return false;
            return true;
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
    }
}
