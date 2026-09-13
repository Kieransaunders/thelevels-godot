using System;
using System.Numerics;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Levels;

/// <summary>The authored opening of The Raised Way. The port's map stays in RaisedWay.</summary>
public static class FirstCrossing
{
    public const int SurvivorCount = 12;
    public const float WaterLine = 2.4f;
    public const float RoadHeight = 3.1f;
    public static readonly Vector3 Camp = new(18f, 0f, 0f);
    public static readonly Vector3 NearBank = new(7f, 0f, 0f);
    public static readonly Vector3 FarBank = new(-11f, 0f, 0f);
    public static readonly Vector3 EarthBank = new(15f, 0f, -9f);

    public static SimulationConfig Configuration() => new()
    {
        EarthCapacity = 45f, WaterCapacity = 3f, BrushRadius = 2.4f, TransferRate = 12f
    };

    public static void Fill(int resolution, float worldSize, float minHeight, float maxHeight,
        float[] terrain, float[] water)
    {
        RaisedWay.Fill(resolution, worldSize, minHeight, maxHeight, terrain, water);
        float cell = worldSize / (resolution - 1);
        float half = worldSize * .5f;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            float wx = x * cell - half, wz = z * cell - half;
            int i = x + z * resolution;
            float h = terrain[i];
            // Readable dry banks, with the old road leading straight to the missing section.
            float local = System.Math.Clamp((14f - MathF.Abs(wz)) / 5f, 0f, 1f)
                        * System.Math.Clamp((27f - MathF.Abs(wx)) / 5f, 0f, 1f);
            h += (RoadHeight - h) * local;
            float supply = Vector2.Distance(new(wx, wz), new(EarthBank.X, EarthBank.Z));
            h += 2.8f * MathF.Exp(-supply * supply / 20f);
            // A slack-water channel spans the map, so the unbuilt crossing cannot be bypassed.
            float channel = System.Math.Clamp((5f - MathF.Abs(wx)) / 2f, 0f, 1f);
            h += (.9f - h) * channel;
            terrain[i] = System.Math.Clamp(h, minHeight, maxHeight);
            // One hydrostatic surface: the opening does not drain itself while the player learns.
            water[i] = MathF.Max(0f, WaterLine - terrain[i]);
        }
    }

    /// <summary>
    /// Ground the forest must leave alone: the corridor the road runs along, and the
    /// supply bank the player scoops from. Groves anywhere else are welcome.
    /// </summary>
    public static bool KeepClear(Vector3 at) =>
        (MathF.Abs(at.Z) < 5f && at.X > -16f && at.X < 25f)
        || Vector2.Distance(new(at.X, at.Z), new(EarthBank.X, EarthBank.Z)) < 6f;
}
