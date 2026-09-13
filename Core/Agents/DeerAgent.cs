using System;
using System.Numerics;
using TheLevels.Core.Math;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

public enum DeerState { Graze, Wander, Flee, Dead }

public enum DeerDeath { None, Burned, Drowned }

/// <summary>
/// One deer: grazes, wanders within sight of the herd, wades shallows, and bolts from
/// fire. Burns in flame and drowns in deep water like the druids, but swims longer
/// (four seconds) and probes further (3 m). Engine-free; <c>Agents/WildlifeView.cs</c>
/// gives it a body and swings its legs.
/// </summary>
public sealed class DeerAgent
{
    public const float WanderSpeed = 1.35f;
    public const float FleeSpeed = 5.4f;
    public const float WadeDepth = 0.32f;
    public const float DrownDepth = 0.70f;
    public const float DrownAfterSeconds = 4f;
    public const float FleeRadius = 12f;
    public const float ProbeDistance = 3f;
    private const float ArriveDistance = 0.9f;

    private static readonly float[] AvoidAngles = { 40f, -40f, 80f, -80f, 120f, -120f, 160f, -160f };

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private readonly Random rng;
    private Vector2 wanderTarget;
    private float drownTimer;
    private float grazeTimer;

    public DeerAgent(Vector3 position, float heading, HeightfieldSimulation heightfield,
        FireSimulation fire, int seed)
    {
        this.heightfield = heightfield;
        this.fire = fire;
        rng = new Random(seed);
        Position = position;
        Heading = heading;
        wanderTarget = new Vector2(position.X, position.Z);
        grazeTimer = 1f + (float)rng.NextDouble() * 3f;
    }

    public Vector3 Position { get; private set; }
    /// <summary>Facing as a yaw angle in radians about +Y, simulation space.</summary>
    public float Heading { get; private set; }
    public DeerState State { get; private set; } = DeerState.Graze;
    public DeerDeath Death { get; private set; } = DeerDeath.None;
    public float DeathTimer { get; private set; }
    /// <summary>Accumulated stride — the view swings legs against this phase.</summary>
    public float WalkPhase { get; private set; }
    /// <summary>Current ground speed, for leg-swing amplitude and graze settles.</summary>
    public float Speed { get; private set; }
    /// <summary>0..1 head-down blend while grazing, for the view's neck pose.</summary>
    public float GrazeAmount { get; private set; }
    public bool Wading { get; private set; }
    public bool IsDead => State == DeerState.Dead;

    /// <summary>Herd centroid pull on wander targets, set by <see cref="HerdManager"/> each tick.</summary>
    public Vector2? HerdBias { get; set; }

    public void Tick(float delta, Vector2? fireThreatCenter)
    {
        if (State == DeerState.Dead) { DeathTimer += delta; Speed = 0f; return; }

        float depth = heightfield.SampleWater(Position);
        Wading = depth > WadeDepth;

        if (FireAt(Position) > 0.1f) { Die(DeerDeath.Burned); return; }

        if (depth > DrownDepth)
        {
            drownTimer += delta;
            if (drownTimer > DrownAfterSeconds) { Die(DeerDeath.Drowned); return; }
        }
        else drownTimer = MathF.Max(0f, drownTimer - delta * 1.5f);

        var here = new Vector2(Position.X, Position.Z);
        bool threatened = fireThreatCenter.HasValue
            && Vector2.Distance(here, fireThreatCenter.Value) < FleeRadius;
        if (threatened) State = DeerState.Flee;
        else if (State == DeerState.Flee) State = DeerState.Wander;

        if (State == DeerState.Graze)
        {
            grazeTimer -= delta;
            GrazeAmount = MathF.Min(1f, GrazeAmount + delta * 2.5f);
            Speed = 0f;
            if (grazeTimer <= 0f)
            {
                wanderTarget = PickWanderTarget(here);
                State = DeerState.Wander;
            }
            Position = SetY(Position);
            return;
        }

        GrazeAmount = MathF.Max(0f, GrazeAmount - delta * 3f);

        Vector2 goal;
        float pace;
        if (State == DeerState.Flee && fireThreatCenter.HasValue)
        {
            goal = here + Vector2.Normalize(here - fireThreatCenter.Value) * 14f;
            pace = FleeSpeed;
        }
        else
        {
            goal = wanderTarget;
            pace = WanderSpeed;
            if (Vector2.Distance(here, goal) < ArriveDistance)
            {
                State = DeerState.Graze;
                grazeTimer = 2.5f + (float)rng.NextDouble() * 5f;
                Position = SetY(Position);
                return;
            }
        }

        Vector2 desired = goal - here;
        if (desired.LengthSquared() > 0.04f) desired = Vector2.Normalize(desired);
        desired = AvoidHazards(Position, desired);

        pace *= Wading ? 0.5f : 1f;
        if (desired != Vector2.Zero)
        {
            Position = ClampToWorld(new Vector3(
                Position.X + desired.X * pace * delta, 0f, Position.Z + desired.Y * pace * delta));
            Heading = DruidAgent.TurnToward(Heading, MathF.Atan2(desired.X, desired.Y),
                (State == DeerState.Flee ? 14f : 7f) * delta);
            WalkPhase += pace * delta * 2.4f;
            Speed = pace;
        }
        else Speed = 0f;
        Position = SetY(Position);
    }

    /// <summary>A graze-length stroll: mostly random, pulled a third of the way to the herd.</summary>
    private Vector2 PickWanderTarget(Vector2 here)
    {
        float angle = (float)rng.NextDouble() * MathF.PI * 2f;
        float distance = 3f + (float)rng.NextDouble() * 6f;
        var at = here + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
        return HerdBias.HasValue ? Vector2.Lerp(at, HerdBias.Value, 0.3f) : at;
    }

    private Vector3 SetY(Vector3 position)
    {
        position.Y = heightfield.SampleSurface(position) + (Wading ? -0.15f : 0f);
        return position;
    }

    private Vector2 AvoidHazards(Vector3 position, Vector2 desired)
    {
        if (SafeStep(position, desired)) return desired;
        foreach (float degrees in AvoidAngles)
        {
            Vector2 turned = Rotate(desired, degrees * (MathF.PI / 180f));
            if (SafeStep(position, turned)) return turned;
        }
        return Vector2.Zero; // boxed in by water and fire — waits like the druids do
    }

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

    private static Vector2 Rotate(Vector2 v, float radians)
        => new(v.X * MathF.Cos(radians) - v.Y * MathF.Sin(radians),
               v.X * MathF.Sin(radians) + v.Y * MathF.Cos(radians));

    private void Die(DeerDeath cause)
    {
        Death = cause;
        State = DeerState.Dead;
        DeathTimer = 0f;
    }
}
