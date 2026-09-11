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

    private static readonly Color Peat = new(.16f, .10f, .055f);
    private static readonly Color Clay = new(.34f, .23f, .13f);
    private static readonly Color Moss = new(.23f, .31f, .12f);
    private static readonly Color Tor = new(.38f, .42f, .24f);
    private static readonly Color Reeds = new(.38f, .46f, .15f);
    private static readonly Color Charcoal = new(.055f, .045f, .04f);
    private static readonly Color FlameLow = new(.85f, .22f, .04f);
    private static readonly Color FlameHigh = new(1f, .86f, .38f);

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
            Color ground = elevation < .15f ? Peat.Lerp(Clay, elevation / .15f)
                : elevation < .45f ? Clay.Lerp(Moss, (elevation - .15f) / .30f)
                : Moss.Lerp(Tor, Mathf.Clamp((elevation - .45f) / .55f, 0, 1));
            if (depth > .001f) ground = ground.Lerp(new Color(.12f, .16f, .12f), .35f);
            ground = ground.Lerp(Reeds, fire.GetFuel(x, z) * .55f);
            if (fire.GetCharred(x, z)) ground = Charcoal.Lerp(ground, .12f);
            float flame = fire.GetFire(x, z);
            if (flame > 0) ground = ground.Lerp(FlameLow.Lerp(FlameHigh, flame), .92f);
            terrainColors[i] = ground;
            Color body = new Color(.30f, .52f, .55f).Lerp(new Color(.04f, .16f, .28f), Mathf.Clamp(depth * .7f, 0, 1));
            waterColors[i] = new Color(body, depth <= 0 ? 0 : Mathf.Clamp(.15f + depth * 4f, 0, 1));
        }
        Upload(terrainMesh, terrainArrays, terrainVertices, terrainNormals, terrainColors, terrainMaterial);
        Upload(waterMesh, waterArrays, waterVertices, waterNormals, waterColors, waterMaterial);
        dirty = false;
        RebuildCount++;
        LastUpdateMilliseconds = watch.Elapsed.TotalMilliseconds;
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
