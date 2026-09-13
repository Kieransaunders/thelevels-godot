using System;
using System.Numerics;
using TheLevels.Core.Math;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

public enum FrogState { Sit, Hop, Swim, Submerged, Dead }

public enum FrogDeath { None, Burned }

/// <summary>
/// One frog: lives on the water margin — sits and croaks, hops along the bank, swims
/// for shore when it drifts deep, and dives under when fire comes near. Only dry land
/// can burn it (the fire grid never holds flame on wet cells). Engine-free;
/// <c>Agents/WildlifeView.cs</c> gives it a body and pulses its throat.
/// </summary>
public sealed class FrogAgent
{
    public const float HopAirSeconds = 0.40f;
    public const float HopHeight = 0.22f;
    public const float ThreatRadius = 7f;
    public const float SubmergedSeconds = 4f;
    public const float SwimDepth = 0.18f;   // deeper than this means swimming
    public const float DiveDepth = 0.12f;   // water deep enough to dive into
    public const float MaxLandingDepth = 0.25f;

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private readonly Random rng;
    private readonly float croakPhase;
    private Vector3 hopFrom, hopTo;
    private float timer;
    private float clock;

    public FrogAgent(Vector3 position, float heading, HeightfieldSimulation heightfield,
        FireSimulation fire, int seed)
    {
        this.heightfield = heightfield;
        this.fire = fire;
        rng = new Random(seed);
        Position = position;
        Heading = heading;
        croakPhase = (float)rng.NextDouble() * MathF.PI * 2f;
        timer = 0.5f + (float)rng.NextDouble() * 3f;
    }

    public Vector3 Position { get; private set; }
    /// <summary>Facing as a yaw angle in radians about +Y, simulation space.</summary>
    public float Heading { get; private set; }
    public FrogState State { get; private set; } = FrogState.Sit;
    public FrogDeath Death { get; private set; } = FrogDeath.None;
    public float DeathTimer { get; private set; }
    /// <summary>0..1 arc progress while airborne; the view adds the hop parabola.</summary>
    public float AirProgress { get; private set; }
    /// <summary>0..1 throat swell while croaking, for the view.</summary>
    public float CroakPulse { get; private set; }
    public int Hops { get; private set; }
    public bool IsDead => State == FrogState.Dead;

    public void Tick(float delta, Vector2? fireThreatCenter)
    {
        if (State == FrogState.Dead) { DeathTimer += delta; CroakPulse = 0f; return; }
        clock += delta;

        // Only dry land burns a frog — wet cells never carry fire in the grid.
        if (FireAt(Position) > 0.1f) { Die(FrogDeath.Burned); return; }

        float depth = heightfield.SampleWater(Position);
        var here = new Vector2(Position.X, Position.Z);
        bool threatened = fireThreatCenter.HasValue
            && Vector2.Distance(here, fireThreatCenter.Value) < ThreatRadius;

        switch (State)
        {
            case FrogState.Sit:
                timer -= delta;
                Croak();
                if (timer > 0f && !threatened) break;
                if (threatened && depth > DiveDepth)
                {
                    State = FrogState.Submerged;
                    timer = SubmergedSeconds;
                    CroakPulse = 0f;
                    break;
                }
                BeginHop(here, threatened ? fireThreatCenter!.Value : null, depth);
                break;

            case FrogState.Hop:
                timer += delta;
                float t = timer / HopAirSeconds;
                if (t >= 1f)
                {
                    Position = new Vector3(hopTo.X, heightfield.SampleSurface(hopTo), hopTo.Z);
                    AirProgress = 0f;
                    float landing = heightfield.SampleWater(hopTo);
                    if (landing > SwimDepth) { State = FrogState.Swim; timer = 0f; }
                    else { State = FrogState.Sit; timer = 1.5f + (float)rng.NextDouble() * 3.5f; }
                }
                else
                {
                    var at = Vector2.Lerp(new Vector2(hopFrom.X, hopFrom.Z),
                        new Vector2(hopTo.X, hopTo.Z), t);
                    Position = new Vector3(at.X, heightfield.SampleSurface(new Vector3(at.X, 0f, at.Y)), at.Y);
                    AirProgress = t;
                    CroakPulse = 0f;
                }
                break;

            case FrogState.Swim:
                CroakPulse = 0f;
                if (threatened && depth > DiveDepth) { State = FrogState.Submerged; timer = SubmergedSeconds; break; }
                if (depth < 0.1f) { State = FrogState.Sit; timer = 1f + (float)rng.NextDouble() * 2f; break; }
                // Paddle toward the shallowest of four probes — frogs work back to the bank.
                Vector2 toShallow = ShallowestDirection(here);
                var next = ClampToWorld(new Vector3(
                    Position.X + toShallow.X * 0.55f * delta, 0f, Position.Z + toShallow.Y * 0.55f * delta));
                next.Y = heightfield.SampleSurface(next);
                Position = next;
                Heading = DruidAgent.TurnToward(Heading, MathF.Atan2(toShallow.X, toShallow.Y), 8f * delta);
                break;

            case FrogState.Submerged:
                CroakPulse = 0f;
                timer -= delta;
                if (timer <= 0f)
                {
                    depth = heightfield.SampleWater(Position);
                    State = depth > SwimDepth ? FrogState.Swim : FrogState.Sit;
                    timer = 1f + (float)rng.NextDouble() * 2f;
                }
                break;
        }
    }

    private void Croak()
    {
        // A croak every few seconds: gate the swell to the top of a slow sine.
        float swell = MathF.Max(0f, MathF.Sin(clock * 3.1f + croakPhase));
        CroakPulse = swell * swell * swell;
    }

    private void BeginHop(Vector2 here, Vector2? threat, float depth)
    {
        // Direction: straight away from fire; toward water when stranded dry; else random.
        Vector2 dir;
        if (threat.HasValue) dir = Vector2.Normalize(here - threat.Value);
        else if (depth < 0.02f)
        {
            Vector2 wettest = WettestDirection(here, out float wet);
            dir = wet >= 0.05f ? wettest : new Vector2(MathF.Cos(rng.NextSingle() * MathF.PI * 2f),
                MathF.Sin(rng.NextSingle() * MathF.PI * 2f));
        }
        else
        {
            float angle = (float)rng.NextDouble() * MathF.PI * 2f;
            dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        float distance = threat.HasValue ? 1.5f : 0.5f + (float)rng.NextDouble() * 0.6f;
        // Try a few jitters of the heading until the landing spot is dry enough.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float wobble = attempt == 0 ? 0f : ((float)rng.NextDouble() * 2f - 1f) * attempt * 0.5f;
            var turned = Rotate(dir, wobble);
            var landing = here + turned * distance;
            if (heightfield.SampleWater(new Vector3(landing.X, 0f, landing.Y)) > MaxLandingDepth) continue;
            hopFrom = Position;
            var clamped = ClampToWorld(new Vector3(landing.X, 0f, landing.Y));
            hopTo = clamped;
            Heading = MathF.Atan2(turned.X, turned.Y);
            State = FrogState.Hop;
            timer = 0f;
            Hops++;
            return;
        }
        // Every landing spot around is too wet: stay put and try again shortly.
        timer = 0.8f;
    }

    private Vector2 WettestDirection(Vector2 here, out float wettest)
    {
        wettest = 0f;
        var best = new Vector2(0f, 1f);
        foreach (Vector2 offset in new[]
                 { new Vector2(3f, 0f), new Vector2(-3f, 0f), new Vector2(0f, 3f), new Vector2(0f, -3f) })
        {
            float depth = heightfield.SampleWater(new Vector3(here.X + offset.X, 0f, here.Y + offset.Y));
            if (depth > wettest) { wettest = depth; best = Vector2.Normalize(offset); }
        }
        return best;
    }

    private Vector2 ShallowestDirection(Vector2 here)
    {
        float shallowest = float.MaxValue;
        var best = new Vector2(0f, 1f);
        foreach (Vector2 offset in new[]
                 { new Vector2(2.5f, 0f), new Vector2(-2.5f, 0f), new Vector2(0f, 2.5f), new Vector2(0f, -2.5f) })
        {
            float depth = heightfield.SampleWater(new Vector3(here.X + offset.X, 0f, here.Y + offset.Y));
            if (depth < shallowest) { shallowest = depth; best = Vector2.Normalize(offset); }
        }
        return best;
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

    private void Die(FrogDeath cause)
    {
        Death = cause;
        State = FrogState.Dead;
        DeathTimer = 0f;
    }
}
