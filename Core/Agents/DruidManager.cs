using System;
using System.Collections.Generic;
using System.Numerics;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

/// <summary>
/// The band of druids: spawns them east of the rhyne, scans for the fire threat that makes
/// them panic, and completes the ritual once every survivor stands inside the haven.
/// Engine-free; <c>Agents/DruidView.cs</c> gives the agents bodies and the haven its stones.
/// </summary>
public sealed class DruidManager
{
    public const float ThreatScanInterval = 0.25f;
    public const float PanicRadius = 8f;

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private readonly List<DruidAgent> agents = new();
    private readonly Random random;
    private readonly int druidCount;
    private float threatTimer;
    private bool threatActive;
    private Vector2 threatCenter;

    public DruidManager(HeightfieldSimulation heightfield, FireSimulation fire, int druidCount = 8, int seed = 7001)
    {
        this.heightfield = heightfield;
        this.fire = fire;
        this.druidCount = druidCount;
        random = new Random(seed);
        SpawnAll();
    }

    public Vector3 SpawnCenter { get; set; } = new(14f, 0f, 6f);
    public Vector3 HavenCenter { get; set; } = new(-8f, 0f, -4f);
    public float HavenRadius { get; set; } = 4.2f;

    public event Action? RitualCompleted;
    /// <summary>Raised when the list of agents is replaced, so views can rebuild bodies.</summary>
    public event Action? Respawned;

    public bool RitualComplete { get; private set; }
    public int AliveCount { get; private set; }
    public int Total => agents.Count;
    public IReadOnlyList<DruidAgent> Agents => agents;

    /// <summary>Druids keep walking while water and fire are paused — call every frame.</summary>
    public void Advance(float delta)
    {
        threatTimer -= delta;
        if (threatTimer <= 0f)
        {
            ScanFireThreat();
            threatTimer = ThreatScanInterval;
        }

        AliveCount = 0;
        foreach (DruidAgent agent in agents)
        {
            agent.Tick(delta, threatActive && !RitualComplete ? threatCenter : null, PanicRadius);
            if (!agent.IsDead) AliveCount++;
        }

        // All-dead must not complete the ritual: at least one survivor, and every survivor home.
        if (!RitualComplete && agents.Count > 0 && AliveCount > 0 && AllLivingAtHaven())
        {
            RitualComplete = true;
            RitualCompleted?.Invoke();
        }
    }

    public void ResetAll()
    {
        RitualComplete = false;
        threatActive = false;
        threatTimer = 0f;
        SpawnAll();
        Respawned?.Invoke();
    }

    private bool AllLivingAtHaven()
    {
        var haven = new Vector2(HavenCenter.X, HavenCenter.Z);
        foreach (DruidAgent agent in agents)
        {
            if (agent.IsDead) continue;
            if (Vector2.Distance(new Vector2(agent.Position.X, agent.Position.Z), haven) > HavenRadius)
                return false;
        }
        return true;
    }

    private void ScanFireThreat()
    {
        int resolution = heightfield.Resolution;
        Vector2 sum = Vector2.Zero;
        int count = 0;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            if (fire.GetFire(x, z) <= 0.05f) continue;
            sum += new Vector2(x, z);
            count++;
        }

        if (count == 0) { threatActive = false; return; }

        threatActive = true;
        Vector2 centroid = sum / count;
        float half = heightfield.WorldSize * 0.5f;
        threatCenter = new Vector2(
            centroid.X * heightfield.CellSize - half,
            centroid.Y * heightfield.CellSize - half);
    }

    private void SpawnAll()
    {
        agents.Clear();
        for (int i = 0; i < druidCount; i++)
        {
            float angle = i / (float)druidCount * MathF.PI * 2f;
            Vector3 at = SpawnCenter + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * 2.6f;
            at.Y = heightfield.SampleSurface(at);
            agents.Add(new DruidAgent(at, HavenCenter, heightfield, fire,
                (float)random.NextDouble() * MathF.PI * 2f));
        }
        AliveCount = agents.Count;
    }
}
