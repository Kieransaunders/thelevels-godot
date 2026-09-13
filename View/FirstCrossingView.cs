using Godot;
using System.Collections.Generic;
using TheLevels.Core.Levels;
using TheLevels.Simulation;
using SimVector = System.Numerics.Vector3;

namespace TheLevels.View;

/// <summary>Camp landmarks and a live route ribbon. Geometry is scenery, not construction physics.</summary>
public partial class FirstCrossingView : Node3D
{
    private SimulationHost host;
    private readonly ImmediateMesh routeMesh = new();
    private readonly List<(Node3D node, SimVector at, float lift)> anchored = new();
    private double refresh;
    private readonly StandardMaterial3D routeMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled
    };

    public void Initialize(SimulationHost simulationHost)
    {
        host = simulationHost;
        AddChild(new MeshInstance3D { Name = "Marked route", Mesh = routeMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var camp = new Node3D { Name = "Survivor camp" };
        AddChild(camp);
        anchored.Add((camp, FirstCrossing.Camp + new SimVector(1, 0, 5), 0));
        // Two salvaged canvas shelters with ridge poles and open ends.
        for (int i = 0; i < 2; i++)
        {
            float x = i * 4f - 2;
            Part(camp, "Canvas left", new BoxMesh { Size = new Vector3(2.3f, .10f, 3.2f) },
                new Vector3(x - .7f, 1, 0), new Color(.53f, .43f, .26f), new Vector3(0, 0, -.75f));
            Part(camp, "Canvas right", new BoxMesh { Size = new Vector3(2.3f, .10f, 3.2f) },
                new Vector3(x + .7f, 1, 0), new Color(.62f, .52f, .32f), new Vector3(0, 0, .75f));
            Part(camp, "Ridge post", new CylinderMesh { TopRadius = .06f, BottomRadius = .08f, Height = 2f },
                new Vector3(x, 1, 1.4f), new Color(.22f, .16f, .10f));
        }
        Part(camp, "Supply cart", new BoxMesh { Size = new Vector3(1.4f, .6f, 2.1f) },
            new Vector3(-4, .6f, 3), new Color(.28f, .20f, .12f));
        for (int i = 0; i < 3; i++)
            Part(camp, "Salvaged timber", new CylinderMesh { TopRadius = .16f, BottomRadius = .18f, Height = 3f },
                new Vector3(2 + i * .4f, .18f, 3.4f), new Color(.35f, .25f, .15f), new Vector3(Mathf.Pi / 2, 0, 0));

        Marker("CAMP", FirstCrossing.Camp + new SimVector(2, 0, 5), new Color(.94f, .85f, .65f));
        Marker("EARTH BANK", FirstCrossing.EarthBank, new Color(1f, .73f, .30f));
        Marker("FIRST GAP", new SimVector(0, 0, 4), new Color(1f, .59f, .46f));
        Marker("FAR BANK", FirstCrossing.FarBank + new SimVector(-1, 0, 4), new Color(.71f, .88f, .75f));

        // Broken road shoulders make the intended line legible even without the overlay.
        for (float x = -13; x <= 23; x += 1.6f)
        {
            if (Mathf.Abs(x) < 5) continue;
            foreach (float z in new[] { -2.4f, 2.4f })
            {
                var stone = Part(this, "Causeway edge", new BoxMesh { Size = new Vector3(.9f, .24f, .45f) },
                    Vector3.Zero, new Color(.41f, .43f, .37f));
                anchored.Add((stone, new SimVector(x, 0, z), .12f));
            }
        }
        Refresh();
    }

    private void Marker(string text, SimVector at, Color color)
    {
        var label = new Label3D { Text = text, FontSize = 30, PixelSize = .014f,
            Modulate = color, OutlineSize = 6, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true };
        AddChild(label);
        anchored.Add((label, at, 3.5f));
    }

    private static MeshInstance3D Part(Node parent, string name, Mesh mesh, Vector3 position, Color color, Vector3 rotation = default)
    {
        var part = new MeshInstance3D { Name = name, Mesh = mesh, Position = position, Rotation = rotation,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = .95f } };
        parent.AddChild(part);
        return part;
    }

    public override void _Process(double delta)
    {
        if (host == null) return;
        refresh -= delta;
        if (refresh > 0) return;
        refresh = .15;
        Refresh();
    }

    private void Refresh()
    {
        foreach (var (node, at, lift) in anchored)
            node.Position = WorldCoordinates.ToGodot(at.X, host.Heightfield.SampleSurface(at) + lift, at.Z);
        routeMesh.ClearSurfaces();
        routeMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, routeMaterial);
        for (float x = -11; x < 18; x += .4f)
        {
            var at = new SimVector(x, 0, 0);
            bool safe = host.Mission.SafeGround(at)
                && host.Mission.CanTraverse(at, at + new SimVector(.4f, 0, 0));
            routeMesh.SurfaceSetColor(!safe ? new Color(1f, .25f, .15f)
                : host.Mission.RouteSafe ? new Color(.42f, 1f, .65f) : new Color(.94f, .86f, .65f));
            Vertex(x, -.12f); Vertex(x + .3f, -.12f); Vertex(x, .12f);
            Vertex(x + .3f, -.12f); Vertex(x + .3f, .12f); Vertex(x, .12f);
        }
        routeMesh.SurfaceEnd();
    }

    private void Vertex(float x, float z)
    {
        float y = host.Heightfield.SampleSurface(new SimVector(x, 0, z)) + .13f;
        routeMesh.SurfaceAddVertex(WorldCoordinates.ToGodot(x, y, z));
    }
}
