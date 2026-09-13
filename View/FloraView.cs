using Godot;
using System.Collections.Generic;
using TheLevels.Core.Simulation;
using TheLevels.Core.Vegetation;
using TheLevels.Simulation;

namespace TheLevels.View;

/// <summary>
/// Bodies for the forest: one trunk MultiMesh and one canopy MultiMesh per species
/// (six draw calls for the whole wood). All logic lives in <see cref="ForestManager"/>;
/// this node only builds instances and copies state each frame — terrain height is
/// re-sampled per publish so trees ride the god hand and erosion, and instance
/// colours carry growth, ember glow and charring through VertexColorUseAsAlbedo.
/// </summary>
public partial class FloraView : Node3D
{
    private static readonly Color Charred = new(.055f, .045f, .04f);
    private static readonly Color EmberLow = new(.9f, .3f, .05f);
    private static readonly Color EmberHigh = new(1f, .8f, .35f);
    private static readonly Color Waterlogged = new(.28f, .26f, .18f);

    private ForestManager forest;
    private HeightfieldSimulation heightfield;
    private readonly Dictionary<TreeSpecies, SpeciesBatch> batches = new();

    private sealed class SpeciesBatch
    {
        public List<TreeAgent> Trees;
        public MultiMeshInstance3D Trunks, Canopies;
        public Color TrunkColour, CanopyColour;
        public float TrunkHeight, CanopyLift;
    }

    public ForestManager Forest => forest;

    public void Initialize(SimulationHost host)
    {
        heightfield = host.Heightfield;
        forest = new ForestManager(heightfield, host.Fire,
            keepClear: host.Mission != null ? TheLevels.Core.Levels.FirstCrossing.KeepClear : null);
        forest.Respawned += Build;
        Build();
    }

    public override void _Process(double delta)
    {
        if (forest == null) return;
        forest.Advance((float)delta);
        PublishAll();
    }

    public override void _ExitTree()
    {
        if (forest != null) forest.Respawned -= Build;
    }

    /// <summary>R reset: fresh forest, same seed, fuel re-deposited.</summary>
    public void ResetAll() => forest?.ResetAll();

    /// <summary>Trunk-base world height for one tree — the P8 terrain-tracking check.</summary>
    public float TrunkBaseWorldY(TreeAgent tree) =>
        WorldCoordinates.ToGodot(tree.Position.X, heightfield.SampleTerrain(tree.Position), tree.Position.Z).Y;

    /// <summary>Rebuilds the instance buffers; called on spawn and every respawn.</summary>
    private void Build()
    {
        foreach (SpeciesBatch batch in batches.Values)
        {
            RemoveChild(batch.Trunks);
            RemoveChild(batch.Canopies);
            batch.Trunks.QueueFree();
            batch.Canopies.QueueFree();
        }
        batches.Clear();

        foreach (var group in GroupBySpecies())
        {
            TreeSpecies species = group.Key;
            float trunkHeight = species switch { TreeSpecies.Rowan => 2.2f, TreeSpecies.Willow => 1.2f, _ => 1.5f };
            var batch = new SpeciesBatch
            {
                Trees = group.Value,
                TrunkHeight = trunkHeight,
                CanopyLift = species switch { TreeSpecies.Rowan => 2.5f, TreeSpecies.Willow => 1.5f, _ => 2.05f },
                TrunkColour = species switch
                {
                    TreeSpecies.Rowan => new Color(.30f, .21f, .15f),
                    TreeSpecies.Willow => new Color(.26f, .22f, .16f),
                    _ => new Color(.33f, .24f, .17f)
                },
                CanopyColour = species switch
                {
                    TreeSpecies.Rowan => new Color(.28f, .38f, .13f),
                    TreeSpecies.Willow => new Color(.24f, .36f, .16f),
                    _ => new Color(.20f, .33f, .12f)
                }
            };

            // Rowan burns like a candle flame; the willow is a wide mop; the oak a rounded blob.
            Mesh trunkMesh = species switch
            {
                TreeSpecies.Rowan => new CylinderMesh { TopRadius = .05f, BottomRadius = .08f, Height = 2.2f, RadialSegments = 6 },
                TreeSpecies.Willow => new CylinderMesh { TopRadius = .08f, BottomRadius = .12f, Height = 1.2f, RadialSegments = 6 },
                _ => new CylinderMesh { TopRadius = .09f, BottomRadius = .14f, Height = 1.5f, RadialSegments = 6 }
            };
            Mesh canopyMesh = species switch
            {
                TreeSpecies.Rowan => new CylinderMesh { TopRadius = .03f, BottomRadius = .62f, Height = 1.9f, RadialSegments = 6 },
                TreeSpecies.Willow => new SphereMesh { Radius = 1.2f, Height = .95f, RadialSegments = 7, Rings = 2 },
                _ => new SphereMesh { Radius = .85f, Height = 1.5f, RadialSegments = 6, Rings = 3 }
            };

            batch.Trunks = MakeBatch($"{species} Trunks", trunkMesh, batch.Trees.Count);
            batch.Canopies = MakeBatch($"{species} Canopies", canopyMesh, batch.Trees.Count);
            AddChild(batch.Trunks);
            AddChild(batch.Canopies);
            batches[species] = batch;
        }
        PublishAll();
    }

    private MultiMeshInstance3D MakeBatch(string name, Mesh mesh, int count)
    {
        // Instance colours only reach the shader when the material multiplies by vertex colour.
        if (mesh is PrimitiveMesh primitive)
            primitive.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .95f };
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            UseColors = true,
            InstanceCount = count
        };
        return new MultiMeshInstance3D { Name = name, Multimesh = multiMesh };
    }

    private Dictionary<TreeSpecies, List<TreeAgent>> GroupBySpecies()
    {
        var grouped = new Dictionary<TreeSpecies, List<TreeAgent>>();
        foreach (TreeAgent tree in forest.Trees)
        {
            if (!grouped.TryGetValue(tree.Species, out List<TreeAgent> list))
                grouped[tree.Species] = list = new List<TreeAgent>();
            list.Add(tree);
        }
        return grouped;
    }

    /// <summary>Copies every tree's transform and colours into the instance buffers.</summary>
    public void PublishAll()
    {
        if (forest == null) return;
        foreach (SpeciesBatch batch in batches.Values)
        for (int i = 0; i < batch.Trees.Count; i++)
        {
            TreeAgent tree = batch.Trees[i];
            Vector3 at = WorldCoordinates.ToGodot(tree.Position.X, 0f, tree.Position.Z);
            float ground = heightfield.SampleTerrain(tree.Position);
            float size = tree.Scale * (0.35f + 0.65f * tree.Growth);
            // A drowned tree sinks to its crown; a charred one stays a standing snag.
            float sink = tree.State == TreeState.Drowned ? tree.Sink * 1.4f : 0f;
            float hide = sink >= 1.35f ? 0f : 1f;

            var basis = new Basis(Vector3.Up, tree.RotationY) * Basis.FromScale(Vector3.One * (size * hide));
            batch.Trunks.Multimesh.SetInstanceTransform(i,
                new Transform3D(basis, at with { Y = ground + batch.TrunkHeight * .5f * size - sink }));
            batch.Canopies.Multimesh.SetInstanceTransform(i,
                new Transform3D(basis, at with { Y = ground + batch.CanopyLift * size - sink }));

            Color canopy = batch.CanopyColour * tree.Tint;
            Color trunk = batch.TrunkColour * Mathf.Max(.85f, tree.Tint);
            switch (tree.State)
            {
                case TreeState.Burning:
                    // Glow while the crown is alight, blacken through the back half of the burn.
                    float flicker = .5f + .5f * Mathf.Sin(tree.BurnTimer * 18f);
                    float burnt = Mathf.Clamp(tree.BurnTimer / TreeAgent.BurnSeconds, 0f, 1f);
                    canopy = canopy
                        .Lerp(EmberLow.Lerp(EmberHigh, flicker), .75f * (1f - burnt * .6f))
                        .Lerp(Charred, burnt * burnt);
                    trunk = trunk.Lerp(Charred, burnt * .7f);
                    break;
                case TreeState.Charred:
                    canopy = Charred;
                    trunk = Charred;
                    break;
                case TreeState.Drowned:
                    canopy = canopy.Lerp(Waterlogged, tree.Sink);
                    break;
            }
            batch.Trunks.Multimesh.SetInstanceColor(i, trunk);
            batch.Canopies.Multimesh.SetInstanceColor(i, canopy);
        }
    }
}
