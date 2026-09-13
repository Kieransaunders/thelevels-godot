using System;
using System.Numerics;
using NUnit.Framework;
using TheLevels.Core.Levels;
using TheLevels.Core.Simulation;

namespace TheLevels.Tests;

/// <summary>
/// The checks every level owes, whatever it is about. A new level gets these for free the
/// moment it joins <see cref="Catalogue.All"/> — the boring failures (a map full of NaN, a
/// map that is entirely underwater, a reset that does not restore) cannot reach a build.
/// Level-specific behaviour still needs its own file; see FirstCrossingTests.
/// </summary>
public sealed class CatalogueTests
{
    public static Level[] Levels => Catalogue.All;

    [Test]
    public void NamesAreUniqueAndResolvable()
    {
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Level level in Catalogue.All)
        {
            Assert.That(level.Name, Is.Not.Empty);
            Assert.That(seen.Add(level.Name), Is.True, $"duplicate level name '{level.Name}'");
            Assert.That(Catalogue.Find(level.Name).Name, Is.EqualTo(level.Name));
            Assert.That(Catalogue.Exists(level.Name), Is.True);
        }
        Assert.That(Catalogue.Find("no-such-level").Name, Is.EqualTo(Catalogue.Default.Name));
        Assert.That(Catalogue.Exists("no-such-level"), Is.False);
    }

    [Test]
    public void NextWalksEveryLevelAndWraps()
    {
        string at = Catalogue.Default.Name;
        for (int i = 0; i < Catalogue.All.Length; i++) at = Catalogue.Next(at).Name;
        Assert.That(at, Is.EqualTo(Catalogue.Default.Name), "cycling the catalogue must return home");
    }

    [TestCaseSource(nameof(Levels))]
    public void MapIsFiniteAndInsideItsClamps(Level level)
    {
        SimulationConfig config = level.Configure();
        var sim = new HeightfieldSimulation(config, level.Fill);
        for (int z = 0; z < sim.Resolution; z++)
        for (int x = 0; x < sim.Resolution; x++)
        {
            float terrain = sim.GetTerrain(x, z), water = sim.GetWater(x, z);
            Assert.That(float.IsFinite(terrain) && float.IsFinite(water), Is.True, $"{level.Name} at {x},{z}");
            Assert.That(terrain, Is.InRange(config.MinimumTerrainHeight, config.MaximumTerrainHeight));
            Assert.That(water, Is.GreaterThanOrEqualTo(0f));
        }
    }

    [TestCaseSource(nameof(Levels))]
    public void MapHasBothDryGroundAndWater(Level level)
    {
        var sim = new HeightfieldSimulation(level.Configure(), level.Fill);
        int dry = 0, wet = 0;
        for (int z = 0; z < sim.Resolution; z++)
        for (int x = 0; x < sim.Resolution; x++)
            if (sim.GetWater(x, z) > 0.01f) wet++; else dry++;
        Assert.That(dry, Is.GreaterThan(0), $"{level.Name} has nowhere to stand");
        Assert.That(wet, Is.GreaterThan(0), $"{level.Name} has no water to shape");
    }

    [TestCaseSource(nameof(Levels))]
    public void SettlesWithoutFaultingOrLosingMass(Level level)
    {
        var sim = new HeightfieldSimulation(level.Configure(), level.Fill);
        var fire = new FireSimulation(sim);
        for (int i = 0; i < 30 * 30; i++) { sim.Advance(1f / 30f); fire.Advance(1f / 30f); }
        Assert.That(sim.LastError, Is.Null, $"{level.Name} faulted: {sim.LastError}");
        Assert.That(sim.InvalidValueCount, Is.Zero);
        Assert.That(sim.NegativeCorrections, Is.Zero);
    }

    [TestCaseSource(nameof(Levels))]
    public void ResetRestoresTheGeneratedMapExactly(Level level)
    {
        var sim = new HeightfieldSimulation(level.Configure(), level.Fill);
        var before = new float[sim.Resolution * sim.Resolution];
        for (int z = 0; z < sim.Resolution; z++)
        for (int x = 0; x < sim.Resolution; x++) before[x + z * sim.Resolution] = sim.GetTerrain(x, z);

        sim.ApplyBrush(Vector3.Zero, MatterType.Earth, true, 2f);
        for (int i = 0; i < 60; i++) sim.Advance(1f / 30f);
        sim.ResetSimulation();

        for (int z = 0; z < sim.Resolution; z++)
        for (int x = 0; x < sim.Resolution; x++)
            Assert.That(sim.GetTerrain(x, z), Is.EqualTo(before[x + z * sim.Resolution]).Within(1e-6f));
        Assert.That(sim.EarthBuffer, Is.Zero);
    }

    [TestCaseSource(nameof(Levels))]
    public void GenerationIsDeterministic(Level level)
    {
        var first = new HeightfieldSimulation(level.Configure(), level.Fill);
        var second = new HeightfieldSimulation(level.Configure(), level.Fill);
        for (int z = 0; z < first.Resolution; z++)
        for (int x = 0; x < first.Resolution; x++)
        {
            Assert.That(second.GetTerrain(x, z), Is.EqualTo(first.GetTerrain(x, z)));
            Assert.That(second.GetWater(x, z), Is.EqualTo(first.GetWater(x, z)));
        }
    }
}
