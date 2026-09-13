using System;
using System.Numerics;
using TheLevels.Core.Agents;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Levels;

public enum CrossingStage { Approach, Build, Crossing, Complete, LostSurvivor }

/// <summary>Opening mission only: an earth crossing, not the later ritual or Water Gate.</summary>
public sealed class FirstCrossingMission
{
    private readonly HeightfieldSimulation terrain;
    private float stableSeconds;
    private bool reachedBank;
    private const float SampleSpacing = .25f;
    public const float MinimumRoadHeight = FirstCrossing.WaterLine + .25f;
    public const float MaximumRoadSlope = 1.25f;

    public FirstCrossingMission(HeightfieldSimulation terrain, FireSimulation fire)
    {
        this.terrain = terrain;
        Band = new DruidManager(terrain, fire, FirstCrossing.SurvivorCount,
            spawnCenter: FirstCrossing.Camp, havenCenter: FirstCrossing.FarBank,
            canTraverse: CanTraverse) { AutoCompleteRitual = false };
        Reset();
    }

    public DruidManager Band { get; }
    public CrossingStage Stage { get; private set; }
    public bool RouteSafe { get; private set; }
    public float SafeFraction { get; private set; }
    public int ArrivedCount { get; private set; }
    public bool CarryingEarth => terrain.EarthBuffer > .1f;

    public void Reset()
    {
        Band.ResetAll();
        Band.SetDestination(FirstCrossing.NearBank);
        Stage = CrossingStage.Approach;
        stableSeconds = 0f;
        reachedBank = false;
        ArrivedCount = 0;
        InspectRoute();
    }

    public void Advance(float delta)
    {
        // Unlike the parity sandbox, tutorial pause pauses the people and mission too.
        if (terrain.Paused || delta <= 0f) return;
        InspectRoute();
        stableSeconds = RouteSafe ? stableSeconds + MathF.Min(delta, .1f) : 0f;
        bool mayCross = stableSeconds >= 1.5f;
        if (!reachedBank)
        {
            reachedBank = true;
            foreach (var agent in Band.Agents)
                if (!agent.IsDead && Distance(agent.Position, FirstCrossing.NearBank) > 1.6f)
                    reachedBank = false;
        }
        if (reachedBank && mayCross) Band.SetDestination(FirstCrossing.FarBank);
        Band.Advance(delta);
        ArrivedCount = 0;
        foreach (var agent in Band.Agents)
            if (!agent.IsDead && Distance(agent.Position, FirstCrossing.FarBank) < 1.6f)
                ArrivedCount++;

        // A casualty cannot silently satisfy an all-survivors objective.
        if (Band.AliveCount != FirstCrossing.SurvivorCount) Stage = CrossingStage.LostSurvivor;
        else if (ArrivedCount == FirstCrossing.SurvivorCount) Stage = CrossingStage.Complete;
        else if (!reachedBank) Stage = CrossingStage.Approach;
        else Stage = mayCross ? CrossingStage.Crossing : CrossingStage.Build;
    }

    public bool SafeGround(Vector3 at) => terrain.ContainsWorldPosition(at)
        && terrain.SampleTerrain(at) >= MinimumRoadHeight
        && terrain.SampleWater(at) <= .15f;

    /// <summary>Check the whole next footstep, including narrow holes missed by the port's probes.</summary>
    public bool CanTraverse(Vector3 from, Vector3 to)
    {
        float distance = Distance(from, to);
        int steps = System.Math.Max(1, (int)MathF.Ceiling(distance / SampleSpacing));
        float previous = terrain.SampleTerrain(from);
        for (int i = 1; i <= steps; i++)
        {
            Vector3 at = Vector3.Lerp(from, to, i / (float)steps);
            float height = terrain.SampleTerrain(at);
            if (!SafeGround(at) || MathF.Abs(height - previous) > MaximumRoadSlope * distance / steps + .01f)
                return false;
            previous = height;
        }
        return true;
    }

    private void InspectRoute()
    {
        int good = 0, total = 0;
        // Inspect a two-metre-wide ribbon, not just a centre point that can hide a hole.
        for (float x = FirstCrossing.FarBank.X; x <= FirstCrossing.NearBank.X; x += SampleSpacing)
        for (int lane = -2; lane <= 2; lane++)
        {
            Vector3 at = new(x, 0f, lane * .5f);
            bool safe = SafeGround(at) && CanTraverse(at, at + new Vector3(SampleSpacing, 0, 0));
            if (safe) good++;
            total++;
        }
        RouteSafe = good == total;
        SafeFraction = good / (float)total;
    }

    private static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));
}
