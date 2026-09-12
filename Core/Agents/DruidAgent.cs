using System;
using System.Numerics;
using TheLevels.Core.Math;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

public enum DruidState { Travel, Panic, Dead }

public enum DruidDeath { None, Drowned, Burned }

/// <summary>
/// One druid: walks to the haven stones, wades shallow water, steers around deep water
/// and flame, panics near fire, drowns when submerged and burns when the fire reaches it.
/// Engine-free so the Godot node stays a thin body — see <see cref="DruidManager"/>.
/// </summary>
public sealed class DruidAgent
{
    public const float WalkSpeed = 2.5f;
    public const float WadeDepth = 0.30f;
    public const float DrownDepth = 0.85f;
    public const float DrownAfterSeconds = 2.2f;
    public const float ProbeDistance = 2.4f;
    private const float ArriveDistance = 1.1f;

    private static readonly float[] AvoidAngles = { 38f, -38f, 76f, -76f, 114f, -114f, 152f, -152f };

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private readonly Vector3 target;
    private float drownTimer;
    private float clock;

    public DruidAgent(Vector3 position, Vector3 target, HeightfieldSimulation heightfield,
        FireSimulation fire, float bobPhase)
    {
        this.heightfield = heightfield;
        this.fire = fire;
        this.target = target;
        Position = position;
        BobPhase = bobPhase;
        Heading = MathF.Atan2(target.X - position.X, target.Z - position.Z);
    }

    public Vector3 Position { get; private set; }
    /// <summary>Facing as a yaw angle in radians about +Y, simulation space.</summary>
    public float Heading { get; private set; }
    public DruidState State { get; private set; } = DruidState.Travel;
    public DruidDeath Death { get; private set; } = DruidDeath.None;
    public float DeathTimer { get; private set; }
    public float BobPhase { get; }
    public bool Wading { get; private set; }
    public bool IsDead => State == DruidState.Dead;

    public void Tick(float delta, Vector2? fireThreatCenter, float panicRadius)
    {
        if (State == DruidState.Dead) { DeathTimer += delta; return; }

        clock += delta;
        Vector3 position = Position;
        float depth = heightfield.SampleWater(position);
        Wading = depth > WadeDepth;

        if (FireAt(position) > 0.1f) { Die(DruidDeath.Burned); return; }

        if (depth > DrownDepth)
        {
            drownTimer += delta;
            if (drownTimer > DrownAfterSeconds) { Die(DruidDeath.Drowned); return; }
        }
        else drownTimer = MathF.Max(0f, drownTimer - delta * 2f);

        var here = new Vector2(position.X, position.Z);
        float distanceToThreat = fireThreatCenter.HasValue
            ? Vector2.Distance(here, fireThreatCenter.Value)
            : float.MaxValue;
        if (fireThreatCenter.HasValue && distanceToThreat < panicRadius) State = DruidState.Panic;
        else if (State == DruidState.Panic && distanceToThreat > panicRadius + 2f) State = DruidState.Travel;

        Vector2 goal = State == DruidState.Panic && fireThreatCenter.HasValue
            ? here + Vector2.Normalize(here - fireThreatCenter.Value) * 10f
            : new Vector2(target.X, target.Z);

        // Arrived: stand at the stones and slowly turn, watching the levels.
        if (State == DruidState.Travel && Vector2.Distance(here, goal) < ArriveDistance)
        {
            Heading += 24f * (MathF.PI / 180f) * delta;
            return;
        }

        Vector2 desired = goal - here;
        if (desired.LengthSquared() > 0.04f) desired = Vector2.Normalize(desired);
        desired = AvoidHazards(position, desired);

        float speed = WalkSpeed * (Wading ? 0.45f : 1f) * (State == DruidState.Panic ? 1.7f : 1f);
        bool moving = desired != Vector2.Zero;
        if (moving)
        {
            position += new Vector3(desired.X, 0f, desired.Y) * (speed * delta);
            position = ClampToWorld(position);
            Heading = TurnToward(Heading, MathF.Atan2(desired.X, desired.Y), 10f * delta);
        }

        position.Y = heightfield.SampleSurface(position) + (Wading ? -0.12f : 0f);
        position.Y += MathF.Sin(clock * 7f + BobPhase) * 0.03f * (moving ? 1f : 0.3f);
        Position = position;
    }

    private Vector2 AvoidHazards(Vector3 position, Vector2 desired)
    {
        if (SafeStep(position, desired)) return desired;
        foreach (float degrees in AvoidAngles)
        {
            Vector2 turned = Rotate(desired, degrees * (MathF.PI / 180f));
            if (SafeStep(position, turned)) return turned;
        }
        return Vector2.Zero; // boxed in by water and fire — waits for the druid player
    }

    /// <summary>
    /// Preserved source limitation: the Unity prototype samples only the probe endpoint at
    /// 2.4 m, never the segment in between, so a hazard narrower than the probe — a single
    /// 0.75 m rhyne cell, say — is stepped into rather than avoided. Kept for port parity;
    /// drowning in a hidden channel is a legible outcome the player can bridge.
    /// </summary>
    private bool SafeStep(Vector3 position, Vector2 direction)
    {
        Vector3 probe = position + new Vector3(direction.X, 0f, direction.Y) * ProbeDistance;
        if (!heightfield.ContainsWorldPosition(probe)) return false;
        if (heightfield.SampleWater(probe) > WadeDepth + 0.05f) return false;
        return FireAt(probe) <= 0.05f;
    }

    private float FireAt(Vector3 position)
    {
        heightfield.WorldToGrid(position, out float gx, out float gz);
        return fire.GetFire((int)gx, (int)gz);
    }

    private Vector3 ClampToWorld(Vector3 position)
    {
        float half = heightfield.WorldSize * 0.5f - 2f;
        position.X = SimulationMath.Clamp(position.X, -half, half);
        position.Z = SimulationMath.Clamp(position.Z, -half, half);
        return position;
    }

    /// <summary>Shortest-arc turn from one yaw to another; internal for the wrap test.</summary>
    public static float TurnToward(float from, float to, float t)
    {
        float difference = (to - from + MathF.PI) % (MathF.PI * 2f);
        if (difference < 0f) difference += MathF.PI * 2f;
        return from + (difference - MathF.PI) * SimulationMath.Clamp01(t);
    }

    private static Vector2 Rotate(Vector2 v, float radians)
        => new(v.X * MathF.Cos(radians) - v.Y * MathF.Sin(radians),
               v.X * MathF.Sin(radians) + v.Y * MathF.Cos(radians));

    private void Die(DruidDeath cause)
    {
        Death = cause;
        State = DruidState.Dead;
        DeathTimer = 0f;
    }
}
