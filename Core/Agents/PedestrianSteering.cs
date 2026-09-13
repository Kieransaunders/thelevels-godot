using System;
using System.Numerics;
using TheLevels.Core.Math;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

/// <summary>
/// Foot travel shared by every walking agent: the water/fire probe steering the druids
/// brought over from the Unity prototype, moved verbatim so <see cref="DruidAgent"/> and
/// <see cref="VillagerAgent"/> cannot drift apart. Engine-free.
/// </summary>
public static class PedestrianSteering
{
    public const float WadeDepth = 0.30f;
    public const float DrownDepth = 0.85f;
    public const float DrownAfterSeconds = 2.2f;
    public const float ProbeDistance = 2.4f;

    internal static readonly float[] AvoidAngles = { 38f, -38f, 76f, -76f, 114f, -114f, 152f, -152f };

    public static Vector2 AvoidHazards(HeightfieldSimulation heightfield, FireSimulation fire,
        Vector3 position, Vector2 desired, float wadeDepth)
    {
        if (SafeStep(heightfield, fire, position, desired, wadeDepth)) return desired;
        foreach (float degrees in AvoidAngles)
        {
            Vector2 turned = Rotate(desired, degrees * (MathF.PI / 180f));
            if (SafeStep(heightfield, fire, position, turned, wadeDepth)) return turned;
        }
        return Vector2.Zero; // boxed in by water and fire — waits for the player to help
    }

    /// <summary>
    /// Preserved source limitation: the Unity prototype samples only the probe endpoint at
    /// 2.4 m, never the segment in between, so a hazard narrower than the probe — a single
    /// 0.75 m rhyne cell, say — is stepped into rather than avoided. Kept for port parity;
    /// drowning in a hidden channel is a legible outcome the player can bridge.
    /// </summary>
    public static bool SafeStep(HeightfieldSimulation heightfield, FireSimulation fire,
        Vector3 position, Vector2 direction, float wadeDepth)
    {
        Vector3 probe = position + new Vector3(direction.X, 0f, direction.Y) * ProbeDistance;
        if (!heightfield.ContainsWorldPosition(probe)) return false;
        if (heightfield.SampleWater(probe) > wadeDepth + 0.05f) return false;
        return FireAt(heightfield, fire, probe) <= 0.05f;
    }

    /// <summary>Shortest-arc turn from one yaw to another; internal for the wrap test.</summary>
    public static float TurnToward(float from, float to, float t)
    {
        float difference = (to - from + MathF.PI) % (MathF.PI * 2f);
        if (difference < 0f) difference += MathF.PI * 2f;
        return from + (difference - MathF.PI) * SimulationMath.Clamp01(t);
    }

    public static Vector3 ClampToWorld(Vector3 position, HeightfieldSimulation heightfield)
    {
        float half = heightfield.WorldSize * 0.5f - 2f;
        position.X = SimulationMath.Clamp(position.X, -half, half);
        position.Z = SimulationMath.Clamp(position.Z, -half, half);
        return position;
    }

    public static float FireAt(HeightfieldSimulation heightfield, FireSimulation fire, Vector3 position)
    {
        heightfield.WorldToGrid(position, out float gx, out float gz);
        return fire.GetFire((int)gx, (int)gz);
    }

    private static Vector2 Rotate(Vector2 v, float radians)
        => new(v.X * MathF.Cos(radians) - v.Y * MathF.Sin(radians),
               v.X * MathF.Sin(radians) + v.Y * MathF.Cos(radians));
}
