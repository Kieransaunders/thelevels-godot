using Godot;
using System;
using System.Diagnostics;
using TheLevels.Core.Simulation;

namespace TheLevels.View;

public partial class HeightfieldView : Node3D
{
    private HeightfieldSimulation simulation;
    private FireSimulation fire;
    private readonly ArrayMesh terrainMesh = new();
    private readonly ArrayMesh waterMesh = new();
    private ShaderMaterial terrainMaterial;
    private ShaderMaterial waterMaterial;
    private Vector3[] terrainVertices, waterVertices, terrainNormals, waterNormals;
    private Color[] terrainColors, waterColors;
    private int[] indices;
    private readonly Godot.Collections.Array terrainArrays = new();
    private readonly Godot.Collections.Array waterArrays = new();
    private readonly Stopwatch watch = new();
    private bool dirty = true;
    public double LastUpdateMilliseconds { get; private set; }
    public int RebuildCount { get; private set; }
    public int HeightfieldEvents { get; private set; }
    public int FireEvents { get; private set; }
    public bool IsDirty => dirty;

    // Hard elevation bands after the Somerset concept art: dark peat lowlands, warm
    // clay, deep moss, dry bracken, grey limestone tor. Values sit well below their
    // displayed brightness — ambient, AgX midtone lift and fog raise them ~2.5×.
    private static readonly Color Peat = new(.08f, .055f, .035f);
    private static readonly Color Clay = new(.15f, .10f, .06f);
    private static readonly Color Moss = new(.09f, .14f, .05f);
    private static readonly Color Bracken = new(.16f, .13f, .07f);
    private static readonly Color Tor = new(.17f, .165f, .15f);
    private static readonly Color WetSheen = new(.10f, .13f, .11f);
    private static readonly (float Edge, Color Ground)[] Bands =
        { (0f, Peat), (.16f, Clay), (.34f, Moss), (.56f, Bracken), (.82f, Tor) };
    private static readonly Color Reeds = new(.38f, .46f, .15f);
    private static readonly Color Charcoal = new(.055f, .045f, .04f);
    private static readonly Color FlameLow = new(.85f, .22f, .04f);
    private static readonly Color FlameHigh = new(1f, .86f, .38f);

    // Maps solver m/s into the water mesh's 0..1 flow channel; ~2.2 m/s saturates.
    private const float FlowToFoam = .45f;
    // Maps water depth into the terrain mesh's alpha channel for shallow caustics.
    private const float DepthToCaustic = 1.6f;

    public void Initialize(HeightfieldSimulation heightfield, FireSimulation fireSimulation)
    {
        simulation = heightfield;
        fire = fireSimulation;
        ProcessPriority = 100;
        int resolution = simulation.Resolution;
        int count = resolution * resolution;
        terrainVertices = new Vector3[count]; waterVertices = new Vector3[count];
        terrainNormals = new Vector3[count]; waterNormals = new Vector3[count];
        terrainColors = new Color[count]; waterColors = new Color[count];
        var uvs = new Vector2[count];
        indices = new int[(resolution - 1) * (resolution - 1) * 6];
        float half = simulation.WorldSize * .5f;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            int i = x + z * resolution;
            terrainVertices[i] = WorldCoordinates.ToGodot(x * simulation.CellSize - half, 0, z * simulation.CellSize - half);
            waterVertices[i] = terrainVertices[i];
            uvs[i] = new Vector2(x / (float)(resolution - 1), z / (float)(resolution - 1));
        }
        int t = 0;
        for (int z = 0; z < resolution - 1; z++)
        for (int x = 0; x < resolution - 1; x++)
        {
            int a = x + z * resolution, b = a + 1, c = a + resolution, d = c + 1;
            // After Z reflection these are Godot's clockwise front faces when viewed above.
            indices[t++] = a; indices[t++] = c; indices[t++] = b;
            indices[t++] = b; indices[t++] = c; indices[t++] = d;
        }
        terrainArrays.Resize((int)Mesh.ArrayType.Max);
        waterArrays.Resize((int)Mesh.ArrayType.Max);
        foreach (var arrays in new[] { terrainArrays, waterArrays })
        {
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = indices;
        }
        terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/terrain.gdshader") };
        waterMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/water.gdshader") };
        AddChild(new MeshInstance3D { Name = "Terrain", Mesh = terrainMesh });
        AddChild(new MeshInstance3D { Name = "Water", Mesh = waterMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        simulation.StateChanged += OnHeightfieldChanged;
        fire.StateChanged += OnFireChanged;
        PublishChanges();
    }

    private void OnHeightfieldChanged() { HeightfieldEvents++; dirty = true; }
    private void OnFireChanged() { FireEvents++; dirty = true; }
    public override void _Process(double delta) => PublishChanges();

    public void PublishChanges()
    {
        if (!dirty || simulation == null) return;
        watch.Restart();
        int resolution = simulation.Resolution;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            int i = x + z * resolution;
            float height = simulation.GetTerrain(x, z), depth = simulation.GetWater(x, z);
            terrainVertices[i].Y = height;
            waterVertices[i].Y = height + Mathf.Max(depth, .015f);
            float elevation = Mathf.Clamp((height - .2f) / 11.8f, 0, 1);
            Color ground = Palette(elevation);
            ground = ground.Lerp(WetSheen, Mathf.Clamp(depth * 3f, 0f, 1f) * .45f);
            // Reeds golden the low wet ground only; on the hills the tint erased the
            // elevation bands and painted the whole map sand-coloured.
            float reed = fire.GetFuel(x, z) * Mathf.Clamp(1f - elevation * 1.4f, 0f, 1f);
            ground = ground.Lerp(Reeds, reed * .4f);
            if (fire.GetCharred(x, z)) ground = Charcoal.Lerp(ground, .12f);
            float flame = fire.GetFire(x, z);
            if (flame > 0) ground = ground.Lerp(FlameLow.Lerp(FlameHigh, flame), .92f);
            terrainColors[i] = new Color(ground, Mathf.Clamp(depth * DepthToCaustic, 0f, 1f));
            Color body = new Color(.30f, .52f, .55f).Lerp(new Color(.04f, .16f, .28f), Mathf.Clamp(depth * .7f, 0, 1));
            var water = new Color(body, depth <= 0 ? 0 : Mathf.Clamp(.15f + depth * 4f, 0, 1));
            // COLOR.r is the whitewater drive: normalized flow speed for the water shader.
            // The shader ignores G/B (it re-derives body colour from true per-pixel thickness).
            water.R = Mathf.Clamp(simulation.FlowSpeed(x, z) * FlowToFoam, 0f, 1f);
            waterColors[i] = water;
        }
        Upload(terrainMesh, terrainArrays, terrainVertices, terrainNormals, terrainColors, terrainMaterial);
        Upload(waterMesh, waterArrays, waterVertices, waterNormals, waterColors, waterMaterial);
        dirty = false;
        RebuildCount++;
        LastUpdateMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>Points the water shader's analytic light at the scene sun.</summary>
    public void SetSunDirection(Vector3 direction) =>
        waterMaterial.SetShaderParameter("light_direction", direction);

    private static Color Palette(float elevation)
    {
        const float blend = .04f;
        for (int b = 0; b < Bands.Length - 1; b++)
        {
            float edge = Bands[b + 1].Edge;
            if (elevation < edge + blend)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((elevation - (edge - blend)) / (blend * 2f), 0f, 1f));
                return Bands[b].Ground.Lerp(Bands[b + 1].Ground, t);
            }
        }
        return Bands[^1].Ground;
    }

    private void Upload(ArrayMesh mesh, Godot.Collections.Array arrays, Vector3[] vertices, Vector3[] normals, Color[] colors, Material material)
    {
        Array.Clear(normals);
        for (int t = 0; t < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            Vector3 normal = (vertices[c] - vertices[a]).Cross(vertices[b] - vertices[a]);
            normals[a] += normal; normals[b] += normal; normals[c] += normal;
        }
        for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].Normalized();
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        // AddSurfaceFromArrays computes the bounds from the newly uploaded vertices.
    }

    public override void _ExitTree()
    {
        if (simulation != null) simulation.StateChanged -= OnHeightfieldChanged;
        if (fire != null) fire.StateChanged -= OnFireChanged;
        terrainArrays.Dispose(); waterArrays.Dispose();
        terrainMesh.Dispose(); waterMesh.Dispose();
        terrainMaterial?.Dispose(); waterMaterial?.Dispose();
    }
}
