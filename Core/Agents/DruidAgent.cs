using System;
using System.Numerics;
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
    public const float WadeDepth = PedestrianSteering.WadeDepth;
    public const float DrownDepth = PedestrianSteering.DrownDepth;
    public const float DrownAfterSeconds = PedestrianSteering.DrownAfterSeconds;
    public const float ProbeDistance = PedestrianSteering.ProbeDistance;
    private const float ArriveDistance = 1.1f;

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private Vector3 target;
    private readonly Func<Vector3, Vector3, bool>? canTraverse;
    private float drownTimer;
    private float clock;

    public DruidAgent(Vector3 position, Vector3 target, HeightfieldSimulation heightfield,
        FireSimulation fire, float bobPhase, Func<Vector3, Vector3, bool>? canTraverse = null)
    {
        this.heightfield = heightfield;
        this.fire = fire;
        this.target = target;
        this.canTraverse = canTraverse;
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

    public void SetDestination(Vector3 destination) => target = destination;

    public void Tick(float delta, Vector2? fireThreatCenter, float panicRadius)
    {
        if (State == DruidState.Dead) { DeathTimer += delta; return; }

        clock += delta;
        Vector3 position = Position;
        float depth = heightfield.SampleWater(position);
        Wading = depth > WadeDepth;

        if (PedestrianSteering.FireAt(heightfield, fire, position) > 0.1f) { Die(DruidDeath.Burned); return; }

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
        desired = PedestrianSteering.AvoidHazards(heightfield, fire, position, desired, WadeDepth);

        float speed = WalkSpeed * (Wading ? 0.45f : 1f) * (State == DruidState.Panic ? 1.7f : 1f);
        bool moving = desired != Vector2.Zero;
        if (moving)
        {
            // The shared steering helpers move and turn; canTraverse is the mission's veto,
            // so a survivor cannot step into a hole the route inspector already rejected.
            Vector3 next = PedestrianSteering.ClampToWorld(
                position + new Vector3(desired.X, 0f, desired.Y) * (speed * delta), heightfield);
            if (canTraverse == null || canTraverse(position, next)) position = next;
            else moving = false;
            Heading = PedestrianSteering.TurnToward(Heading, MathF.Atan2(desired.X, desired.Y), 10f * delta);
        }

        position.Y = heightfield.SampleSurface(position) + (Wading ? -0.12f : 0f);
        position.Y += MathF.Sin(clock * 7f + BobPhase) * 0.03f * (moving ? 1f : 0.3f);
        Position = position;
    }

    /// <summary>Shortest-arc turn, delegated to the shared pedestrian steering (pinned by the druid wrap test).</summary>
    public static float TurnToward(float from, float to, float t)
        => PedestrianSteering.TurnToward(from, to, t);

    private void Die(DruidDeath cause)
    {
        Death = cause;
        State = DruidState.Dead;
        DeathTimer = 0f;
    }
}
