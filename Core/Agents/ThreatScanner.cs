using System.Numerics;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

/// <summary>
/// Centroid of every burning cell, in world space — the fire threat the wildlife
/// flees from. Shared by the herd and the frog chorus (the druids keep their own
/// private scan for Unity-port parity).
/// </summary>
public static class ThreatScanner
{
    public static Vector2? Scan(HeightfieldSimulation heightfield, FireSimulation fire)
    {
        int resolution = heightfield.Resolution;
        Vector2 sum = Vector2.Zero;
        int count = 0;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            if (fire.GetFire(x, z) <= 0.05f) continue;
            sum += new Vector2(x, z);
            count++;
        }

        if (count == 0) return null;
        Vector2 centroid = sum / count;
        float half = heightfield.WorldSize * 0.5f;
        return new Vector2(
            centroid.X * heightfield.CellSize - half,
            centroid.Y * heightfield.CellSize - half);
    }
}
