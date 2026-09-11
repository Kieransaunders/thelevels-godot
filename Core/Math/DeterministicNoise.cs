using System;

namespace TheLevels.Core.Math;

/// <summary>
/// Deterministic value noise replacing Unity's Mathf.PerlinNoise, which has no
/// output-identical Godot equivalent. Accepted deviation per the revision 3 design:
/// the map is not pixel-identical to Unity's; scale and amplitude are preserved
/// (call sites keep their 0.055 input scale and 0.45 amplitude).
/// Integer hash mixing is fixed (unlike string/object hash codes, which are
/// randomized per process), so terrain generation is reproducible across runs
/// and platforms.
/// </summary>
public static class DeterministicNoise
{
    /// <summary>Smooth value noise in [0, 1). Corner values are hashed lattice points.</summary>
    public static float ValueNoise(float x, float z)
    {
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        float tx = Smooth(x - x0);
        float tz = Smooth(z - z0);

        float v00 = Lattice(x0, z0);
        float v10 = Lattice(x0 + 1, z0);
        float v01 = Lattice(x0, z0 + 1);
        float v11 = Lattice(x0 + 1, z0 + 1);

        float a = SimulationMath.Lerp(v00, v10, tx);
        float b = SimulationMath.Lerp(v01, v11, tx);
        return SimulationMath.Lerp(a, b, tz);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>Wang-hash style bit mix of a lattice point into [0, 1).</summary>
    private static float Lattice(int x, int z)
    {
        uint h = 0x9E3779B9u;
        h ^= (uint)x * 0x85EBCA6Bu;
        h ^= (uint)z * 0xC2B2AE35u;
        h ^= h >> 15;
        h *= 0x2545F491u;
        h ^= h >> 13;
        return (h & 0x00FFFFFFu) / 16777216f;
    }
}
