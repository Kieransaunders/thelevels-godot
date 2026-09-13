using System;
using System.Numerics;
using NUnit.Framework;
using TheLevels.Core.Levels;
using TheLevels.Core.Simulation;

namespace TheLevels.Tests;

public sealed class FirstCrossingTests
{
    private HeightfieldSimulation sim = null!;
    private FireSimulation fire = null!;
    private FirstCrossingMission mission = null!;

    [SetUp]
    public void SetUp()
    {
        sim = new HeightfieldSimulation(FirstCrossing.Configuration(), FirstCrossing.Fill);
        fire = new FireSimulation(sim);
        mission = new FirstCrossingMission(sim, fire);
    }

    private void Advance(float seconds)
    {
        for (int i = 0; i < (int)(seconds * 30); i++)
        {
            sim.Advance(1f / 30f);
            fire.Advance(1f / 30f);
            mission.Advance(1f / 30f);
        }
    }

    [Test]
    public void WaitingNeverSolvesTheGapOrLosesASurvivor()
    {
        Advance(60);
        Assert.That(mission.Band.Total, Is.EqualTo(12));
        Assert.That(mission.Band.AliveCount, Is.EqualTo(12));
        Assert.That(mission.Stage, Is.EqualTo(CrossingStage.Build));
        Assert.That(mission.ArrivedCount, Is.Zero);
        Assert.That(mission.Band.RitualComplete, Is.False);
        Assert.That(sim.SampleWater(Vector3.Zero), Is.GreaterThan(1f));
    }

    [Test]
    public void PlayerBrushesBuildAUsableCrossingForAllTwelveAndResetRestoresTheOpening()
    {
        Advance(8);
        BuildWithBrushes();
        Advance(30);
        Assert.That(mission.Stage, Is.EqualTo(CrossingStage.Complete), Profile());
        Assert.That(mission.ArrivedCount, Is.EqualTo(12));
        Assert.That(mission.Band.RitualComplete, Is.False, "first gap is not the final ritual");
        sim.ResetSimulation(); fire.ResetFire(); mission.Reset();
        Assert.That(mission.Stage, Is.EqualTo(CrossingStage.Approach));
        Assert.That(mission.RouteSafe, Is.False);
        Assert.That(mission.ArrivedCount, Is.Zero);
        Assert.That(mission.Band.AliveCount, Is.EqualTo(12));
        Assert.That(sim.EarthBuffer, Is.Zero);
    }

    [Test]
    public void PauseStopsMissionAndPeople()
    {
        var at = mission.Band.Agents[0].Position;
        sim.Paused = true;
        Advance(3);
        Assert.That(mission.Band.Agents[0].Position, Is.EqualTo(at));
        Assert.That(mission.Stage, Is.EqualTo(CrossingStage.Approach));
    }

    [Test]
    public void DrainingWaterDoesNotCountAsAnEarthBridge()
    {
        for (int i = 0; i < 20; i++)
        {
            sim.ApplyBrush(Vector3.Zero, MatterType.Water, true, 1f);
            Assert.That(sim.WaterBuffer, Is.LessThanOrEqualTo(3.001f));
            sim.ApplyBrush(new Vector3(30, 0, 25), MatterType.Water, false, 1f);
        }
        mission.Advance(.1f);
        Assert.That(mission.SafeGround(Vector3.Zero), Is.False);
        Assert.That(mission.RouteSafe, Is.False);
    }

    [Test]
    public void ANewHoleInvalidatesTheRouteAndCannotBeSteppedAcross()
    {
        BuildWithBrushes();
        sim.ApplyBrush(new Vector3(30, 0, 25), MatterType.Earth, false, 4f);
        Assert.That(sim.ApplyBrush(Vector3.Zero, MatterType.Earth, true, 4f), Is.GreaterThan(0));
        mission.Advance(.1f);
        Assert.That(mission.RouteSafe, Is.False);
        Assert.That(mission.CanTraverse(new Vector3(2, 0, 0), new Vector3(-2, 0, 0)), Is.False);
    }

    [Test]
    public void LosingOnePersonCannotCompleteTheObjective()
    {
        var at = mission.Band.Agents[0].Position;
        sim.ApplyBrush(new Vector3(0, 0, 20), MatterType.Water, true, 1f);
        // A local flood can still be a real setback; repeat small transfers to submerge the camp.
        for (int i = 0; i < 40; i++)
        {
            sim.ApplyBrush(new Vector3(0, 0, 20), MatterType.Water, true, 1f);
            sim.ApplyBrush(at, MatterType.Water, false, 1f);
        }
        // Hold the flood in place for this death-condition test; the water solver is tested separately.
        for (int i = 0; i < 100; i++) mission.Advance(1f / 30f);
        Assert.That(mission.Band.AliveCount, Is.LessThan(12));
        Assert.That(mission.Stage, Is.EqualTo(CrossingStage.LostSurvivor));
        Assert.That(mission.Band.RitualComplete, Is.False);
    }

    private void BuildWithBrushes()
    {
        // Small, ordinary pours along the marked route. Scoop from different parts of the
        // generous earth bank so the exercise also checks the level has usable material.
        for (int i = 0; i < 260 && !mission.RouteSafe; i++)
        {
            float lowest = float.MaxValue, targetX = 0;
            for (float x = -4.5f; x <= 4.5f; x += .75f)
            {
                float h = sim.SampleTerrain(new Vector3(x, 0, 1f));
                if (h < lowest) { lowest = h; targetX = x; }
            }
            if (sim.EarthBuffer < 2f)
            {
                float angle = i * 2.4f;
                sim.ApplyBrush(FirstCrossing.EarthBank + new Vector3(MathF.Cos(angle) * 3, 0, MathF.Sin(angle) * 3),
                    MatterType.Earth, true, 4f);
            }
            sim.ApplyBrush(new Vector3(targetX, 0, 0), MatterType.Earth, false, .15f);
            Advance(.2f);
        }
        Advance(3);
        Assert.That(mission.RouteSafe, Is.True, Profile());
    }

    private string Profile()
    {
        string result = $"{mission.Stage}, {mission.SafeFraction:P0} safe, {mission.ArrivedCount} across. ";
        for (float x = -5; x <= 5; x++)
            result += $"x={x}: h={sim.SampleTerrain(new(x, 0, 1)):0.00} w={sim.SampleWater(new(x, 0, 1)):0.00}; ";
        return result;
    }
}
