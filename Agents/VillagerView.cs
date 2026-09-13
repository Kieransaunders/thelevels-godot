using Godot;
using System;
using System.Collections.Generic;
using TheLevels.Core.Agents;
using TheLevels.Simulation;
using TheLevels.View;

namespace TheLevels.Agents;

/// <summary>
/// Bodies for the settlement's villagers — men, women and mothers with babies strapped
/// on their backs — plus the roundhouses and hearth they live around. All logic lives in
/// <see cref="VillagerManager"/>; this node only builds meshes and copies poses each frame.
/// Villagers keep strolling while water and fire are paused, so this ticks off _Process.
/// </summary>
public partial class VillagerView : Node3D
{
    private static readonly Color CharredColour = new(.07f, .06f, .05f);
    private static readonly Color Skin = new(.76f, .62f, .48f);
    private static readonly Color Hair = new(.15f, .12f, .09f);
    private static readonly Color Leather = new(.23f, .16f, .10f);
    private static readonly Color Wood = new(.30f, .21f, .13f);
    private static readonly Color Linen = new(.82f, .78f, .68f);

    private VillagerManager village;
    private Node3D bodies;
    private readonly List<Node3D> figures = new();

    public VillagerManager Village => village;

    public void Initialize(SimulationHost host)
    {
        village = new VillagerManager(host.Heightfield, host.Fire);
        village.Respawned += RebuildBodies;
        BuildSettlement(host);
        bodies = new Node3D { Name = "Bodies" };
        AddChild(bodies);
        RebuildBodies();
    }

    public override void _Process(double delta)
    {
        if (village == null) return;
        village.Advance((float)delta);
        for (int i = 0; i < figures.Count && i < village.Total; i++)
            Pose(figures[i], village.Agents[i], (float)delta);
    }

    public override void _ExitTree()
    {
        if (village == null) return;
        village.Respawned -= RebuildBodies;
    }

    /// <summary>R resets the world: fresh villagers back at the settlement.</summary>
    public void ResetAll() => village?.ResetAll();

    private void Pose(Node3D figure, VillagerAgent agent, float delta)
    {
        Vector3 at = WorldCoordinates.ToGodot(agent.Position.X, agent.Position.Y, agent.Position.Z);
        if (agent.IsDead)
        {
            if (agent.Death == VillagerDeath.Drowned)
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
        var rng = new Random(5110);
        for (int i = 0; i < village.Total; i++)
        {
            Node3D figure = BuildFigure(rng, village.Agents[i], i);
            bodies.AddChild(figure);
            figures.Add(figure);
            Pose(figure, village.Agents[i], 0f);
        }
    }

    private Node3D BuildFigure(Random rng, VillagerAgent agent, int index)
    {
        var figure = new Node3D { Name = $"Villager {index}" };
        bool mother = agent.Kind == VillagerKind.Mother;
        bool man = agent.Kind == VillagerKind.Man;

        if (man)
        {
            Color tunic = new Color(.27f, .32f, .40f).Lerp(new Color(.47f, .37f, .22f), (float)rng.NextDouble());
            // Legs peek below the tunic so a tipped-over burn reads as a person.
            foreach (float side in new[] { -1f, 1f })
                figure.AddChild(Part("Leg", new CapsuleMesh { Radius = .05f, Height = .34f },
                    new Vector3(.07f * side, .17f, 0f), new Color(.20f, .18f, .16f), .2f));
            figure.AddChild(Part("Tunic", new CylinderMesh { TopRadius = .13f, BottomRadius = .17f, Height = .60f },
                new Vector3(0f, .64f, 0f), tunic, .25f));
            figure.AddChild(Part("Belt", new CylinderMesh { TopRadius = .16f, BottomRadius = .16f, Height = .05f },
                new Vector3(0f, .52f, 0f), Leather, .15f));
            AddArms(figure, .185f, .70f, .44f, tunic * .9f);
            figure.AddChild(Part("Head", new SphereMesh { Radius = .1f, Height = .2f },
                new Vector3(0f, 1.03f, .01f), Skin, .3f));
            figure.AddChild(Part("Hair", new SphereMesh { Radius = .105f, Height = .13f },
                new Vector3(0f, 1.07f, .02f), Hair, .25f));
        }
        else
        {
            Color dress = mother
                ? new Color(.44f, .29f, .23f).Lerp(new Color(.31f, .35f, .25f), (float)rng.NextDouble())
                : new Color(.47f, .27f, .23f).Lerp(new Color(.60f, .56f, .48f), (float)rng.NextDouble());
            figure.AddChild(Part("Dress", new CylinderMesh { TopRadius = .095f, BottomRadius = .23f, Height = .80f },
                new Vector3(0f, .40f, 0f), dress, .25f));
            figure.AddChild(Part("Shoulders", new BoxMesh { Size = new Vector3(.24f, .14f, .13f) },
                new Vector3(0f, .86f, 0f), dress * .85f, .25f));
            AddArms(figure, .15f, .66f, .40f, dress * .9f);
            figure.AddChild(Part("Head", new SphereMesh { Radius = .095f, Height = .19f },
                new Vector3(0f, .94f, .01f), Skin, .3f));
            figure.AddChild(Part("Hair", new SphereMesh { Radius = .1f, Height = .12f },
                new Vector3(0f, .98f, .02f), Hair, .25f));
            figure.AddChild(Part("HairFall", new BoxMesh { Size = new Vector3(.14f, .24f, .05f) },
                new Vector3(0f, .84f, .09f), Hair, .25f));
            if (mother) AddCradleboard(figure);
        }
        return figure;
    }

    private static void AddArms(Node3D figure, float spread, float height, float length, Color sleeve)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            var arm = Part("Arm", new CapsuleMesh { Radius = .042f, Height = length },
                new Vector3(spread * 1f, height, 0f), sleeve, .2f);
            arm.RotationDegrees = new Vector3(0f, 0f, -8f * side);
            figure.AddChild(arm);
        }
    }

    /// <summary>
    /// The baby's cradleboard: a wooden board against the mother's back (+Z local is behind
    /// once the pose yaw puts -Z on the heading), the swaddled baby peeking over its rim,
    /// leather straps across her chest and shoulders.
    /// </summary>
    private static void AddCradleboard(Node3D figure)
    {
        var board = Part("Cradleboard", new BoxMesh { Size = new Vector3(.22f, .36f, .09f) },
            new Vector3(0f, .60f, .17f), Wood, .15f);
        board.RotationDegrees = new Vector3(-15f, 0f, 0f);
        figure.AddChild(board);

        var swaddle = Part("Swaddle", new CapsuleMesh { Radius = .045f, Height = .14f },
            new Vector3(0f, .68f, .16f), Linen, .2f);
        swaddle.RotationDegrees = new Vector3(-15f, 0f, 0f);
        figure.AddChild(swaddle);

        var babyHead = Part("Baby", new SphereMesh { Radius = .06f, Height = .12f },
            new Vector3(0f, .78f, .18f), Skin, .3f);
        babyHead.RotationDegrees = new Vector3(-15f, 0f, 0f);
        figure.AddChild(babyHead);

        figure.AddChild(Part("ChestStrap", new BoxMesh { Size = new Vector3(.24f, .045f, .02f) },
            new Vector3(0f, .72f, -.08f), Leather, .15f));
        foreach (float side in new[] { -1f, 1f })
        {
            var strap = Part("ShoulderStrap", new BoxMesh { Size = new Vector3(.035f, .26f, .02f) },
                new Vector3(.085f * side, .80f, .04f), Leather, .15f);
            strap.RotationDegrees = new Vector3(32f, 0f, 0f);
            figure.AddChild(strap);
        }
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

    /// <summary>Four wattle roundhouses facing a shared hearth, on the flank of the mound.</summary>
    private void BuildSettlement(SimulationHost host)
    {
        var settlement = new Node3D { Name = "Settlement" };
        AddChild(settlement);
        var daub = SharedMaterial(new Color(.44f, .40f, .31f), .1f);
        var thatch = SharedMaterial(new Color(.48f, .40f, .22f), .15f);
        var dark = SharedMaterial(new Color(.12f, .09f, .07f), .1f);
        var rng = new Random(5110);
        const int houses = 4;
        for (int h = 0; h < houses; h++)
        {
            float angle = h / (float)houses * MathF.PI * 2f + .4f;
            var at = new System.Numerics.Vector3(
                village.HomeCenter.X + Mathf.Cos(angle) * 5.4f, 0f,
                village.HomeCenter.Z + Mathf.Sin(angle) * 5.4f);
            float ground = host.Heightfield.SampleTerrain(at);
            var house = new Node3D { Name = $"Roundhouse {h}" };
            house.Position = WorldCoordinates.ToGodot(at.X, ground, at.Z);
            house.RotationDegrees = new Vector3(0f, (float)(rng.NextDouble() * 40f - 20f), 0f);
            house.AddChild(new MeshInstance3D
            {
                Name = "Walls", MaterialOverride = daub,
                Mesh = new CylinderMesh { TopRadius = 1.5f, BottomRadius = 1.6f, Height = 1.2f },
                Position = new Vector3(0f, .6f, 0f)
            });
            house.AddChild(new MeshInstance3D
            {
                Name = "Roof", MaterialOverride = thatch,
                Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 2.0f, Height = 1.0f },
                Position = new Vector3(0f, 1.7f, 0f)
            });
            // Door on the wall facing the settlement's centre.
            var inward = new System.Numerics.Vector3(-Mathf.Cos(angle), 0f, -Mathf.Sin(angle));
            house.AddChild(new MeshInstance3D
            {
                Name = "Door", MaterialOverride = dark,
                Mesh = new BoxMesh { Size = new Vector3(.5f, .8f, .07f) },
                Position = new Vector3(inward.X * 1.58f, .4f, -inward.Z * 1.58f),
                RotationDegrees = new Vector3(0f, Mathf.RadToDeg(Mathf.Atan2(inward.X, -inward.Z)), 0f)
            });
            settlement.AddChild(house);
        }

        var hearthCenter = new System.Numerics.Vector3(village.HomeCenter.X, 0f, village.HomeCenter.Z);
        float hearthGround = host.Heightfield.SampleTerrain(hearthCenter);
        var hearth = new Node3D { Name = "Hearth" };
        hearth.Position = WorldCoordinates.ToGodot(hearthCenter.X, hearthGround, hearthCenter.Z);
        hearth.AddChild(new MeshInstance3D
        {
            Name = "Ash", MaterialOverride = SharedMaterial(new Color(.09f, .08f, .07f), .1f),
            Mesh = new CylinderMesh { TopRadius = .42f, BottomRadius = .42f, Height = .07f },
            Position = new Vector3(0f, .035f, 0f)
        });
        var stone = SharedMaterial(new Color(.42f, .42f, .39f), .08f);
        for (int s = 0; s < 6; s++)
        {
            float a = s / 6f * MathF.PI * 2f;
            hearth.AddChild(new MeshInstance3D
            {
                Name = $"Hearth Stone {s}", MaterialOverride = stone,
                Mesh = new BoxMesh { Size = new Vector3(.22f, .18f, .16f) },
                Position = new Vector3(Mathf.Cos(a) * .55f, .09f, Mathf.Sin(a) * .55f),
                RotationDegrees = new Vector3(0f, Mathf.RadToDeg(a), 0f)
            });
        }
        settlement.AddChild(hearth);
    }
}
