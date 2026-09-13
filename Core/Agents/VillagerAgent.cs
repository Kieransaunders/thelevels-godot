using System;
using System.Numerics;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

public enum VillagerKind { Man, Woman, Mother }

public enum VillagerState { Stroll, Dwell, Panic, Dead }

public enum VillagerDeath { None, Drowned, Burned }

/// <summary>
/// One villager of the settlement: men, women, and mothers with a baby strapped to their
/// back (the baby is view-only — the agent carries just the kind). Villagers stroll and
/// dwell inside the home radius, share the druids' water/fire probe steering, panic from
/// fire, and drown or burn by the same rules. Engine-free; <see cref="VillagerManager"/>
/// owns the band and the Godot view gives them bodies.
/// </summary>
public sealed class VillagerAgent
{
    public const float WalkSpeed = 1.7f;
    private const float ArriveDistance = 0.9f;
    private const float PanicRunDistance = 12f;
    private const float IdleTurnSpeed = 40f * (MathF.PI / 180f);

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private readonly Random random;
    private readonly float homeRadius;
    private Vector3 home;
    private Vector2 wanderTarget;
    private float drownTimer;
    private float clock;
    private float dwellTimer;

    public VillagerAgent(VillagerKind kind, Vector3 position, Vector3 home, float homeRadius,
        HeightfieldSimulation heightfield, FireSimulation fire, int seed)
    {
        Kind = kind;
        this.heightfield = heightfield;
        this.fire = fire;
        this.home = home;
        this.homeRadius = homeRadius;
        random = new Random(seed);
        Position = position;
        BobPhase = (float)random.NextDouble() * MathF.PI * 2f;
        Heading = (float)random.NextDouble() * MathF.PI * 2f;
        wanderTarget = new Vector2(position.X, position.Z);
    }

    public Vector3 Position { get; private set; }
    /// <summary>Facing as a yaw angle in radians about +Y, simulation space.</summary>
    public float Heading { get; private set; }
    public VillagerKind Kind { get; }
    public VillagerState State { get; private set; } = VillagerState.Stroll;
    public VillagerDeath Death { get; private set; } = VillagerDeath.None;
    public float DeathTimer { get; private set; }
    public float BobPhase { get; }
    public bool Wading { get; private set; }
    public bool IsDead => State == VillagerState.Dead;
    /// <summary>Where this villager settles — moves away if fire claims the settlement.</summary>
    public Vector3 Home => home;

    public void Tick(float delta, Vector2? fireThreatCenter, float panicRadius)
    {
        if (IsDead) { DeathTimer += delta; return; }

        clock += delta;
        Vector3 position = Position;
        float depth = heightfield.SampleWater(position);
        Wading = depth > PedestrianSteering.WadeDepth;

        if (PedestrianSteering.FireAt(heightfield, fire, position) > 0.1f) { Die(VillagerDeath.Burned); return; }

        if (depth > PedestrianSteering.DrownDepth)
        {
            drownTimer += delta;
            if (drownTimer > PedestrianSteering.DrownAfterSeconds) { Die(VillagerDeath.Drowned); return; }
        }
        else drownTimer = MathF.Max(0f, drownTimer - delta * 2f);

        var here = new Vector2(position.X, position.Z);
        float distanceToThreat = fireThreatCenter.HasValue
            ? Vector2.Distance(here, fireThreatCenter.Value)
            : float.MaxValue;
        if (fireThreatCenter.HasValue && distanceToThreat < panicRadius) State = VillagerState.Panic;
        else if (State == VillagerState.Panic && distanceToThreat > panicRadius + 2f)
        {
            State = VillagerState.Stroll;
            // The settlement may itself be burning: adopt the refuge as home rather
            // than walk back into the fire.
            if (fireThreatCenter.HasValue
                && Vector2.Distance(new Vector2(home.X, home.Z), fireThreatCenter.Value) < panicRadius)
            {
                home = position;
                wanderTarget = here;
            }
        }

        bool moving = false;
        Vector2 desired = Vector2.Zero;
        if (State == VillagerState.Panic && fireThreatCenter.HasValue)
        {
            desired = Vector2.Normalize(here - fireThreatCenter.Value) * PanicRunDistance;
            moving = true;
        }
        else if (Vector2.Distance(here, new Vector2(home.X, home.Z)) > homeRadius + 1f)
        {
            // Driven or wandered past the bounds: come home.
            desired = new Vector2(home.X, home.Z) - here;
            moving = true;
        }
        else if (State == VillagerState.Dwell)
        {
            dwellTimer -= delta;
            if (dwellTimer <= 0f)
            {
                wanderTarget = PickWanderTarget();
                State = VillagerState.Stroll;
            }
            // Stand the pause out, turning to watch the levels.
            Heading += IdleTurnSpeed * delta;
        }
        else
        {
            desired = wanderTarget - here;
            if (Vector2.Distance(here, wanderTarget) < ArriveDistance)
            {
                State = VillagerState.Dwell;
                dwellTimer = 1.5f + (float)random.NextDouble() * 3f;
                desired = Vector2.Zero;
            }
            else moving = true;
        }

        if (moving)
        {
            if (desired.LengthSquared() > 0.04f) desired = Vector2.Normalize(desired);
            desired = PedestrianSteering.AvoidHazards(heightfield, fire, position, desired,
                PedestrianSteering.WadeDepth);

            float speed = WalkSpeed * (Kind == VillagerKind.Mother ? 0.85f : 1f)
                * (Wading ? 0.45f : 1f) * (State == VillagerState.Panic ? 1.9f : 0.8f);
            if (desired != Vector2.Zero)
            {
                position += new Vector3(desired.X, 0f, desired.Y) * (speed * delta);
                position = PedestrianSteering.ClampToWorld(position, heightfield);
                Heading = PedestrianSteering.TurnToward(Heading, MathF.Atan2(desired.X, desired.Y), 8f * delta);
            }
        }

        position.Y = heightfield.SampleSurface(position) + (Wading ? -0.12f : 0f);
        position.Y += MathF.Sin(clock * 6f + BobPhase) * 0.03f * (moving ? 1f : 0.3f);
        Position = position;
    }

    private Vector2 PickWanderTarget()
    {
        float angle = (float)random.NextDouble() * MathF.PI * 2f;
        float radius = MathF.Sqrt((float)random.NextDouble()) * homeRadius * 0.8f;
        return new Vector2(home.X, home.Z) + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private void Die(VillagerDeath cause)
    {
        Death = cause;
        State = VillagerState.Dead;
        DeathTimer = 0f;
    }
}
