using System;
using System.Collections.Generic;
using System.Numerics;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

/// <summary>
/// The frog chorus: spawns along the water margins (dry bank beside wet ground), scans
/// for fire, and ticks every frog. Engine-free; <c>Agents/WildlifeView.cs</c> gives
/// the frogs bodies and their croaking throat pulse.
/// </summary>
public sealed class FrogManager
{
    public const float ThreatScanInterval = 0.25f;

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private readonly List<FrogAgent> frogs = new();
    private readonly int frogCount;
    private readonly int seed;
    private float threatTimer;
    private Vector2? threatCenter;

    public FrogManager(HeightfieldSimulation heightfieldSimulation, FireSimulation fireSimulation,
        int frogCount = 12, int seed = 5150)
    {
        heightfield = heightfieldSimulation;
        fire = fireSimulation;
        this.frogCount = frogCount;
        this.seed = seed;
        SpawnAll();
    }

    /// <summary>Raised when the chorus is replaced, so views can rebuild bodies.</summary>
    public event Action? Respawned;

    public int AliveCount { get; private set; }
    public int Total => frogs.Count;
    public IReadOnlyList<FrogAgent> Frogs => frogs;

    public void Advance(float delta)
    {
        threatTimer -= delta;
        if (threatTimer <= 0f)
        {
            threatCenter = ThreatScanner.Scan(heightfield, fire);
            threatTimer = ThreatScanInterval;
        }

        foreach (FrogAgent frog in frogs) frog.Tick(delta, threatCenter);
        AliveCount = 0;
        foreach (FrogAgent frog in frogs)
            if (!frog.IsDead) AliveCount++;
    }

    /// <summary>R reset: the same seeded chorus returns to the banks.</summary>
    public void ResetAll()
    {
        threatCenter = null;
        threatTimer = 0f;
        SpawnAll();
        Respawned?.Invoke();
    }

    /// <summary>Margin cells — dry ground with wet ground within ~1.6 m — picked deterministically.</summary>
    private void SpawnAll()
    {
        frogs.Clear();
        var random = new Random(seed);
        float half = heightfield.WorldSize * 0.5f;

        var candidates = new List<Vector2>();
        for (int z = 8; z < heightfield.Resolution - 8; z++)
        for (int x = 8; x < heightfield.Resolution - 8; x++)
        {
            if (heightfield.GetWater(x, z) > 0.02f) continue;
            float worldX = x * heightfield.CellSize - half;
            float worldZ = z * heightfield.CellSize - half;
            bool banked = false;
            foreach (Vector2 offset in new[] { new Vector2(1.6f, 0f), new Vector2(-1.6f, 0f),
                                               new Vector2(0f, 1.6f), new Vector2(0f, -1.6f) })
            {
                float depth = heightfield.SampleWater(new Vector3(worldX + offset.X, 0f, worldZ + offset.Y));
                if (depth > 0.04f && depth < 0.45f) { banked = true; break; }
            }
            if (banked) candidates.Add(new Vector2(worldX, worldZ));
        }

        // Fisher–Yates with the seeded rng, then take well-spaced picks off the top.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        var picked = new List<Vector2>();
        foreach (Vector2 at in candidates)
        {
            if (picked.Count >= frogCount) break;
            bool crowded = false;
            foreach (Vector2 other in picked)
                if (Vector2.Distance(at, other) < 2.5f) { crowded = true; break; }
            if (!crowded) picked.Add(at);
        }

        for (int i = 0; i < picked.Count; i++)
        {
            var at = new Vector3(picked[i].X, heightfield.SampleSurface(new Vector3(picked[i].X, 0f, picked[i].Y)), picked[i].Y);
            frogs.Add(new FrogAgent(at, (float)random.NextDouble() * MathF.PI * 2f,
                heightfield, fire, seed + 31 + i));
        }
        AliveCount = frogs.Count;
    }
}
