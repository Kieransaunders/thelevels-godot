using System;
using System.Collections.Generic;
using System.Numerics;
using TheLevels.Core.Math;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Vegetation;

/// <summary>
/// Scatters groves across the levels: copses of oaks on the clay, rowan on the higher
/// bracken, willow on the wet margins, each seeded and deterministic. Deposits wood
/// fuel on every tree cell so the reed fire can climb into the canopy, and ticks
/// growth, burning and drowning. Engine-free; <c>View/FloraView.cs</c> renders it.
/// </summary>
public sealed class ForestManager
{
    public const int GroveCount = 8;
    public const float MinSpacing = 1.9f;
    public const float FuelDeposit = 0.45f;

    private static readonly Vector3 DruidHaven = new(-8f, 0f, -4f);
    private static readonly Vector3 DruidSpawn = new(14f, 0f, 6f);

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private readonly int targetCount;
    private readonly int seed;
    private readonly List<TreeAgent> trees = new();
    private Random random = new();

    public ForestManager(HeightfieldSimulation heightfieldSimulation, FireSimulation fireSimulation,
        int targetCount = 110, int seed = 1187)
    {
        heightfield = heightfieldSimulation;
        fire = fireSimulation;
        this.targetCount = targetCount;
        this.seed = seed;
        SpawnAll();
    }

    /// <summary>Raised when the tree list is replaced, so views can rebuild instances.</summary>
    public event Action? Respawned;

    public IReadOnlyList<TreeAgent> Trees => trees;
    public int AliveCount { get; private set; }
    public int BurningCount { get; private set; }
    public int CharredCount { get; private set; }
    public int DrownedCount { get; private set; }

    public void Advance(float delta)
    {
        foreach (TreeAgent tree in trees) tree.Tick(delta);
        CountStates();
    }

    /// <summary>R reset: the same seed scatters the same forest, fuel re-deposited.</summary>
    public void ResetAll()
    {
        SpawnAll();
        Respawned?.Invoke();
    }

    private void SpawnAll()
    {
        trees.Clear();
        random = new Random(seed);
        float half = heightfield.WorldSize * 0.5f;

        // Grove centres: dry ground in the vegetation band, clear of the druids' haven
        // and spawn ring, and of each other so the copses read separately.
        var centres = new List<Vector2>();
        for (int attempt = 0; attempt < 400 && centres.Count < GroveCount; attempt++)
        {
            var at = new Vector2(
                (float)(random.NextDouble() * 2 - 1) * (half - 8f),
                (float)(random.NextDouble() * 2 - 1) * (half - 8f));
            if (!GroveSite(at)) continue;
            bool crowded = false;
            foreach (Vector2 other in centres)
                if (Vector2.Distance(at, other) < 12f) { crowded = true; break; }
            if (!crowded) centres.Add(at);
        }

        int perGrove = centres.Count > 0 ? System.Math.Max(1, targetCount / centres.Count) : 0;
        int planted = 0;
        foreach (Vector2 centre in centres)
        {
            int groveTarget = System.Math.Min(perGrove + (planted < targetCount % System.Math.Max(1, centres.Count) ? 1 : 0),
                targetCount - planted);
            for (int t = 0; t < groveTarget; t++)
            {
                Vector3? at = TryPlantNear(centre);
                if (at == null) continue;
                trees.Add(MakeTree(at.Value));
                planted++;
            }
        }
        DepositFuel();
        CountStates();
    }

    private bool GroveSite(Vector2 at)
    {
        if (!heightfield.ContainsWorldPosition(new Vector3(at.X, 0f, at.Y))) return false;
        var probe = new Vector3(at.X, 0f, at.Y);
        if (heightfield.SampleWater(probe) > 0.02f) return false;
        float height = heightfield.SampleTerrain(probe);
        if (height < 0.7f || height > 8.5f) return false;
        if (Vector2.Distance(at, new Vector2(DruidHaven.X, DruidHaven.Z)) < 9f) return false;
        if (Vector2.Distance(at, new Vector2(DruidSpawn.X, DruidSpawn.Z)) < 9f) return false;
        return true;
    }

    private Vector3? TryPlantNear(Vector2 centre)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            float angle = (float)random.NextDouble() * MathF.PI * 2f;
            float radius = (float)random.NextDouble() * 5.2f;
            var at = new Vector2(centre.X + MathF.Cos(angle) * radius, centre.Y + MathF.Sin(angle) * radius);
            if (!GroveSite(at)) continue;
            bool crowded = false;
            foreach (TreeAgent tree in trees)
                if (Vector2.Distance(at, new Vector2(tree.Position.X, tree.Position.Z)) < MinSpacing)
                { crowded = true; break; }
            if (crowded) continue;
            return new Vector3(at.X, heightfield.SampleTerrain(new Vector3(at.X, 0f, at.Y)), at.Y);
        }
        return null;
    }

    private TreeAgent MakeTree(Vector3 at)
    {
        // Species by ground, with value noise jittering the band edges so the stands mix.
        float jitter = (DeterministicNoise.ValueNoise(at.X * 0.13f + 40f, at.Z * 0.13f - 20f) - 0.5f) * 1.4f;
        float height = heightfield.SampleTerrain(at);
        TreeSpecies species = height + jitter < 1.5f ? TreeSpecies.Willow
            : height + jitter > 3.6f ? TreeSpecies.Rowan
            : TreeSpecies.Oak;
        return new TreeAgent(at, species,
            0.85f + (float)random.NextDouble() * 0.30f,
            (float)random.NextDouble() * MathF.PI * 2f,
            0.85f + (float)random.NextDouble() * 0.25f,
            heightfield, fire);
    }

    /// <summary>Wood fuel on every tree cell — call again after any fire reset.</summary>
    private void DepositFuel()
    {
        foreach (TreeAgent tree in trees)
        {
            heightfield.WorldToGrid(tree.Position, out float gx, out float gz);
            fire.AddFuel((int)gx, (int)gz, FuelDeposit);
        }
    }

    private void CountStates()
    {
        int alive = 0, burning = 0, charred = 0, drowned = 0;
        foreach (TreeAgent tree in trees)
        {
            switch (tree.State)
            {
                case TreeState.Alive: alive++; break;
                case TreeState.Burning: burning++; break;
                case TreeState.Charred: charred++; break;
                case TreeState.Drowned: drowned++; break;
            }
        }
        AliveCount = alive;
        BurningCount = burning;
        CharredCount = charred;
        DrownedCount = drowned;
    }
}
