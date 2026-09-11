using System;

namespace TheLevels.Core.Math;

/// <summary>
/// Float helpers mirroring Unity's Mathf semantics exactly where the solver relies on them.
/// Same-named .NET/System methods do not always agree (e.g. Lerp clamping, Approximately),
/// so every Unity call the ported simulation used goes through here.
/// </summary>
public static class SimulationMath
{
    /// <summary>Unity's Mathf.Epsilon: the smallest normalized positive float (not float.Epsilon).</summary>
    public const float Epsilon = 1.17549435e-38f;

    public static float Clamp(float value, float min, float max)
        => value < min ? min : value > max ? max : value;

    public static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;

    public static float Clamp01(float value) => Clamp(value, 0f, 1f);

    /// <summary>Unity's Mathf.Lerp clamps t to [0, 1].</summary>
    public static float Lerp(float a, float b, float t)
    {
        t = Clamp01(t);
        return a + (b - a) * t;
    }

    public static float Max(float a, float b) => a > b ? a : b;
    public static float Min(float a, float b) => a < b ? a : b;

    public static int Max(int a, int b) => a > b ? a : b;
    public static int Min(int a, int b) => a < b ? a : b;

    public static float Abs(float value) => value < 0f ? -value : value;

    public static int FloorToInt(float value) => (int)MathF.Floor(value);
    public static int CeilToInt(float value) => (int)MathF.Ceiling(value);

    public static float Sqrt(float value) => MathF.Sqrt(value);
    public static float Exp(float value) => MathF.Exp(value);
    public static float Pow(float x, float y) => MathF.Pow(x, y);
    public static float Sin(float value) => MathF.Sin(value);

    public static bool IsNaN(float value) => float.IsNaN(value);
    public static bool IsInfinity(float value) => float.IsInfinity(value);

    /// <summary>Unity's Mathf.Approximately: relative tolerance with the Unity epsilon floor.</summary>
    public static bool Approximately(float a, float b)
        => Abs(b - a) < Max(1e-06f * Max(Abs(a), Abs(b)), Epsilon * 8f);

    /// <summary>Vector2.Distance equivalent over grid coordinates.</summary>
    public static float Distance(float ax, float az, float bx, float bz)
        => Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}
