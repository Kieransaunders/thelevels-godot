using System;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Levels;

/// <summary>Populates terrain and water for one level. The whole contract a level has.</summary>
public delegate void LevelFill(int resolution, float worldSize, float minHeight, float maxHeight,
    float[] terrain, float[] water);

/// <summary>
/// One playable level: the name you type at <c>--level=</c>, the solver settings it wants,
/// and the function that draws its map.
/// </summary>
/// <remarks>
/// Deliberately no mission here. <see cref="FirstCrossingMission"/> is the only mission that
/// exists, and one example is not enough to design an interface from — the nouns level two
/// wants (stages? a ritual? a food clock?) are not knowable yet. Config and fill are the parts
/// that genuinely generalise, so those are what the catalogue carries. Promote the mission
/// when there are two to compare.
/// </remarks>
public readonly record struct Level(string Name, string Title,
    Func<SimulationConfig> Configure, LevelFill Fill);

public static class Catalogue
{
    /// <summary>Every level, in play order. Adding one is a line here plus its fill function.</summary>
    public static readonly Level[] All =
    {
        new("first-crossing", "01 / The First Crossing", FirstCrossing.Configuration, FirstCrossing.Fill),
        new("sandbox", "Parity sandbox (RaisedWay)", () => new SimulationConfig(), RaisedWay.Fill),
    };

    public static Level Default => All[0];

    /// <summary>The named level, or the default when the name is missing or unknown.</summary>
    public static Level Find(string name)
    {
        foreach (Level level in All)
            if (string.Equals(level.Name, name, StringComparison.OrdinalIgnoreCase)) return level;
        return Default;
    }

    public static bool Exists(string name)
    {
        foreach (Level level in All)
            if (string.Equals(level.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The level after this one, wrapping — what the in-game level key steps through.</summary>
    public static Level Next(string name)
    {
        for (int i = 0; i < All.Length; i++)
            if (string.Equals(All[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return All[(i + 1) % All.Length];
        return Default;
    }
}
