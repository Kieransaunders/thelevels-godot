using System;
using System.Collections.Generic;
using System.Numerics;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

/// <summary>
/// The deer herd: spawns together on open grassland, scans for fire, and keeps every
/// deer's wander targets biased toward the herd centroid so the group holds together.
/// Engine-free; <c>Agents/WildlifeView.cs</c> gives the deer bodies.
/// </summary>
public sealed class HerdManager
{
    public const float ThreatScanInterval = 0.25f;

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private readonly List<DeerAgent> agents = new();
    private readonly int deerCount;
    private readonly int seed;
    private float threatTimer;
    private Vector2? threatCenter;

    public HerdManager(HeightfieldSimulation heightfieldSimulation, FireSimulation fireSimulation,
        int deerCount = 5, int seed = 8842)
    {
        heightfield = heightfieldSimulation;
        fire = fireSimulation;
        this.deerCount = deerCount;
        this.seed = seed;
        SpawnAll();
    }

    /// <summary>Raised when the herd is replaced, so views can rebuild bodies.</summary>
    public event Action? Respawned;

    public Vector3 SpawnCenter { get; private set; }
    public int AliveCount { get; private set; }
    public int Total => agents.Count;
    public IReadOnlyList<DeerAgent> Agents => agents;

    public void Advance(float delta)
    {
        threatTimer -= delta;
        if (threatTimer <= 0f)
        {
            threatCenter = ThreatScanner.Scan(heightfield, fire);
            threatTimer = ThreatScanInterval;
        }

        var centroid = HerdCentroid;
        foreach (DeerAgent agent in agents)
        {
            agent.HerdBias = centroid;
            agent.Tick(delta, threatCenter);
        }
        AliveCount = 0;
        foreach (DeerAgent agent in agents)
            if (!agent.IsDead) AliveCount++;
    }

    public Vector2 HerdCentroid
    {
        get
        {
            if (agents.Count == 0) return Vector2.Zero;
            Vector2 sum = Vector2.Zero;
            int living = 0;
            foreach (DeerAgent agent in agents)
            {
                if (agent.IsDead) continue;
                sum += new Vector2(agent.Position.X, agent.Position.Z);
                living++;
            }
            return living > 0 ? sum / living : new Vector2(agents[0].Position.X, agents[0].Position.Z);
        }
    }

    /// <summary>R reset: fresh herd at the same seeded spot.</summary>
    public void ResetAll()
    {
        threatCenter = null;
        threatTimer = 0f;
        SpawnAll();
        Respawned?.Invoke();
    }

    private void SpawnAll()
    {
        agents.Clear();
        var random = new Random(seed);
        SpawnCenter = FindGrazing(random);
        for (int i = 0; i < deerCount; i++)
        {
            float angle = i / (float)deerCount * MathF.PI * 2f + (float)random.NextDouble() * 0.5f;
            float radius = 1.4f + (float)random.NextDouble() * 1.6f;
            var at = new Vector3(
                SpawnCenter.X + MathF.Cos(angle) * radius, 0f,
                SpawnCenter.Z + MathF.Sin(angle) * radius);
            at.Y = heightfield.SampleSurface(at);
            agents.Add(new DeerAgent(at, (float)random.NextDouble() * MathF.PI * 2f,
                heightfield, fire, seed + 101 + i));
        }
        AliveCount = agents.Count;
    }

    /// <summary>Open dry grassland away from the druids — seeded, so it is the same every run.</summary>
    private Vector3 FindGrazing(Random random)
    {
        float half = heightfield.WorldSize * 0.5f;
        for (int attempt = 0; attempt < 400; attempt++)
        {
            var at = new Vector3(
                (float)(random.NextDouble() * 2 - 1) * (half - 10f), 0f,
                (float)(random.NextDouble() * 2 - 1) * (half - 10f));
            if (!SiteIsOpen(at)) continue;
            // The whole spawn ring must be dry too, or deer land in the rhyne.
            bool ring = true;
            for (int a = 0; a < 6 && ring; a++)
            {
                float angle = a / 6f * MathF.PI * 2f;
                if (heightfield.SampleWater(new Vector3(at.X + MathF.Cos(angle) * 3f, 0f, at.Z + MathF.Sin(angle) * 3f)) > 0.02f)
                    ring = false;
            }
            if (ring) return at;
        }
        return new Vector3(0f, 0f, 0f);
    }

    private bool SiteIsOpen(Vector3 at)
    {
        if (!heightfield.ContainsWorldPosition(at)) return false;
        if (heightfield.SampleWater(at) > 0.02f) return false;
        float height = heightfield.SampleTerrain(at);
        if (height < 1.2f || height > 7f) return false;
        // Keep clear of the druids' business: haven stones and spawn ring.
        if (Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(-8f, -4f)) < 10f) return false;
        if (Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(14f, 6f)) < 10f) return false;
        return true;
    }
}
