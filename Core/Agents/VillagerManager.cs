using System;
using System.Collections.Generic;
using System.Numerics;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Agents;

/// <summary>
/// The settlement's villagers: five men, four women and three mothers (babies on their
/// backs) spawned on the west flank of the central mound. They keep to the home radius,
/// panic from fire like the druids — fleeing far enough re-homes them if the settlement
/// itself is burning — and never gate anything on reaching a place. Engine-free;
/// <c>Agents/VillagerView.cs</c> gives them bodies, roundhouses and a hearth.
/// </summary>
public sealed class VillagerManager
{
    public const float ThreatScanInterval = 0.25f;
    public const float PanicRadius = 9f;

    // Cycled so any band size keeps the 5:4:3 shape of the full twelve.
    private static readonly VillagerKind[] Composition =
        { VillagerKind.Man, VillagerKind.Woman, VillagerKind.Man, VillagerKind.Mother,
          VillagerKind.Man, VillagerKind.Woman, VillagerKind.Man, VillagerKind.Mother,
          VillagerKind.Woman, VillagerKind.Man, VillagerKind.Mother, VillagerKind.Woman };

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private readonly List<VillagerAgent> villagers = new();
    private readonly int villagerCount;
    private readonly int seed;
    private float threatTimer;
    private bool threatActive;
    private Vector2 threatCenter;

    public VillagerManager(HeightfieldSimulation heightfield, FireSimulation fire,
        int villagerCount = 12, int seed = 9102)
    {
        this.heightfield = heightfield;
        this.fire = fire;
        this.villagerCount = villagerCount;
        this.seed = seed;
        SpawnAll();
    }

    public Vector3 HomeCenter { get; set; } = new(-14f, 0f, 4f);
    public float HomeRadius { get; set; } = 8.5f;

    /// <summary>Raised when the list of villagers is replaced, so views can rebuild bodies.</summary>
    public event Action? Respawned;

    public int AliveCount { get; private set; }
    public int Total => villagers.Count;
    public IReadOnlyList<VillagerAgent> Agents => villagers;

    /// <summary>Villagers keep strolling while water and fire are paused — call every frame.</summary>
    public void Advance(float delta)
    {
        threatTimer -= delta;
        if (threatTimer <= 0f)
        {
            Vector2? center = ThreatScanner.Scan(heightfield, fire);
            threatActive = center.HasValue;
            if (center.HasValue) threatCenter = center.Value;
            threatTimer = ThreatScanInterval;
        }

        AliveCount = 0;
        foreach (VillagerAgent villager in villagers)
        {
            villager.Tick(delta, threatActive ? threatCenter : null, PanicRadius);
            if (!villager.IsDead) AliveCount++;
        }
    }

    public int CountOf(VillagerKind kind)
    {
        int count = 0;
        foreach (VillagerAgent villager in villagers)
            if (villager.Kind == kind) count++;
        return count;
    }

    public void ResetAll()
    {
        threatActive = false;
        threatTimer = 0f;
        SpawnAll();
        Respawned?.Invoke();
    }

    private void SpawnAll()
    {
        villagers.Clear();
        for (int i = 0; i < villagerCount; i++)
        {
            float angle = i / (float)villagerCount * MathF.PI * 2f;
            Vector3 at = HomeCenter + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * 3.2f;
            at.Y = heightfield.SampleSurface(at);
            villagers.Add(new VillagerAgent(Composition[i % Composition.Length], at, HomeCenter,
                HomeRadius, heightfield, fire, seed + i * 977));
        }
        AliveCount = villagers.Count;
    }
}
