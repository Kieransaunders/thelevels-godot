using Godot;
using System;
using System.Collections.Generic;
using TheLevels.Core.Agents;
using TheLevels.Simulation;
using TheLevels.View;

namespace TheLevels.Agents;

/// <summary>
/// Bodies for the druid band and stones for their haven. All logic lives in
/// <see cref="DruidManager"/>; this node only builds meshes and copies poses each frame.
/// Druids keep walking while water and fire are paused, so this ticks off _Process.
/// </summary>
public partial class DruidView : Node3D
{
    private static readonly Color CharredColour = new(.07f, .06f, .05f);

    private DruidManager band;
    private Node3D bodies;
    private readonly List<Node3D> figures = new();

    public DruidManager Band => band;

    public void Initialize(SimulationHost host)
    {
        band = new DruidManager(host.Heightfield, host.Fire);
        band.RitualCompleted += OnRitualCompleted;
        band.Respawned += RebuildBodies;
        BuildHaven(host);
        bodies = new Node3D { Name = "Druids" };
        AddChild(bodies);
        RebuildBodies();
    }

    public override void _Process(double delta)
    {
        if (band == null) return;
        band.Advance((float)delta);
        for (int i = 0; i < figures.Count && i < band.Total; i++) Pose(figures[i], band.Agents[i], (float)delta);
    }

    public override void _ExitTree()
    {
        if (band == null) return;
        band.RitualCompleted -= OnRitualCompleted;
        band.Respawned -= RebuildBodies;
    }

    /// <summary>R resets the world: fresh druids, ritual uncompleted.</summary>
    public void ResetAll() => band?.ResetAll();

    private void Pose(Node3D figure, DruidAgent agent, float delta)
    {
        Vector3 at = WorldCoordinates.ToGodot(agent.Position.X, agent.Position.Y, agent.Position.Z);
        if (agent.IsDead)
        {
            if (agent.Death == DruidDeath.Drowned)
            {
                figure.Position = new Vector3(figure.Position.X, figure.Position.Y - 0.7f * delta, figure.Position.Z);
                if (agent.DeathTimer > 3f) figure.Visible = false;
            }
            else
            {
                // Burned: tip over and rest as a charred figure.
                Char(figure);
                figure.Rotation = new Vector3(
                    Mathf.LerpAngle(figure.Rotation.X, Mathf.Pi * .5f, 5f * delta), figure.Rotation.Y, 0f);
                if (agent.DeathTimer > 5f) figure.Visible = false;
            }
            return;
        }
        figure.Position = at;
        // Reflecting Z at the engine boundary mirrors yaw; +PI puts Godot's -Z forward on the heading.
        figure.Rotation = new Vector3(0f, Mathf.Pi - agent.Heading, 0f);
    }

    private void RebuildBodies()
    {
        foreach (Node3D figure in figures) figure.QueueFree();
        figures.Clear();
        var rng = new Random(4211);
        for (int i = 0; i < band.Total; i++)
        {
            Node3D figure = BuildFigure(rng, i);
            bodies.AddChild(figure);
            figures.Add(figure);
            Pose(figure, band.Agents[i], 0f);
        }
    }

    private Node3D BuildFigure(Random rng, int index)
    {
        var figure = new Node3D { Name = $"Druid {index}" };
        Color robe = new Color(.30f, .27f, .19f).Lerp(new Color(.22f, .30f, .16f), (float)rng.NextDouble());

        figure.AddChild(Part("Robe", new CapsuleMesh { Radius = .17f, Height = 1.12f },
            new Vector3(0f, .34f, 0f), robe, .25f));
        figure.AddChild(Part("Head", new SphereMesh { Radius = .1f, Height = .2f },
            new Vector3(0f, .78f, .02f), new Color(.76f, .62f, .48f), .3f));
        // Godot's cone is a cylinder with no top radius — no hand-built hood mesh needed.
        figure.AddChild(Part("Hood", new CylinderMesh { TopRadius = 0f, BottomRadius = .15f, Height = .22f },
            new Vector3(0f, .8f, 0f), robe * .75f, .25f));
        var staff = Part("Staff", new CylinderMesh { TopRadius = .0175f, BottomRadius = .0175f, Height = 1.6f },
            new Vector3(.2f, .5f, .08f), new Color(.24f, .17f, .11f), .1f);
        staff.RotationDegrees = new Vector3(8f, 0f, -6f);
        figure.AddChild(staff);
        return figure;
    }

    private static MeshInstance3D Part(string name, Mesh mesh, Vector3 at, Color colour, float smoothness)
        => new() { Name = name, Mesh = mesh, Position = at, MaterialOverride = SharedMaterial(colour, smoothness) };

    private static void Char(Node3D figure)
    {
        if (figure.HasMeta("charred")) return;
        figure.SetMeta("charred", true);
        foreach (Node child in figure.GetChildren())
            if (child is MeshInstance3D part && part.MaterialOverride is StandardMaterial3D material)
                material.AlbedoColor = CharredColour;
    }

    private static StandardMaterial3D SharedMaterial(Color colour, float smoothness)
        => new() { AlbedoColor = colour, Roughness = 1f - smoothness, Metallic = 0f };

    private void BuildHaven(SimulationHost host)
    {
        var haven = new Node3D { Name = "Haven Stone Circle" };
        AddChild(haven);
        var stone = SharedMaterial(new Color(.44f, .44f, .41f), .08f);
        var rng = new Random(7001);
        const int stones = 7;
        for (int s = 0; s < stones; s++)
        {
            float angle = s / (float)stones * Mathf.Pi * 2f;
            var at = new System.Numerics.Vector3(
                band.HavenCenter.X + Mathf.Cos(angle) * 3.1f, 0f,
                band.HavenCenter.Z + Mathf.Sin(angle) * 3.1f);
            float ground = host.Heightfield.SampleTerrain(at);
            float height = 1.9f + (float)rng.NextDouble() * .9f;
            haven.AddChild(new MeshInstance3D
            {
                Name = $"Standing Stone {s}",
                Mesh = new BoxMesh { Size = new Vector3(.72f, height, .5f) },
                Position = WorldCoordinates.ToGodot(at.X, ground + height * .42f, at.Z),
                RotationDegrees = new Vector3(
                    (float)(rng.NextDouble() * 8f - 4f), -Mathf.RadToDeg(angle) + 90f,
                    (float)(rng.NextDouble() * 6f - 3f)),
                MaterialOverride = stone
            });
        }
    }

    private void OnRitualCompleted()
    {
        GD.Print("The druids complete their ritual at the stones. The Levels are blessed.");
    }
}
