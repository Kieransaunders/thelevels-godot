using Godot;
using System;
using System.Collections.Generic;
using TheLevels.Core.Agents;
using TheLevels.Simulation;
using TheLevels.View;

namespace TheLevels.Agents;

/// <summary>
/// Bodies for the deer herd and the frog chorus. All logic lives in
/// <see cref="HerdManager"/> and <see cref="FrogManager"/>; this node builds low-poly
/// figures and copies poses each frame — swinging legs against the deer's walk phase,
/// dropping its neck to graze, arcing the frogs over their hops and pulsing their
/// croaking throats. Ticks off _Process so the wildlife keeps living while the water
/// and fire sims are paused, exactly like the druids.
/// </summary>
public partial class WildlifeView : Node3D
{
    private static readonly Color CharredColour = new(.07f, .06f, .05f);

    private HerdManager herd;
    private FrogManager chorus;
    private Node3D deerRoot, frogRoot;
    private readonly List<DeerBody> deer = new();
    private readonly List<Node3D> frogs = new();
    private readonly List<Node3D> throats = new();

    public HerdManager Herd => herd;
    public FrogManager Chorus => chorus;

    public void Initialize(SimulationHost host)
    {
        herd = new HerdManager(host.Heightfield, host.Fire);
        chorus = new FrogManager(host.Heightfield, host.Fire);
        herd.Respawned += RebuildDeer;
        chorus.Respawned += RebuildFrogs;
        deerRoot = new Node3D { Name = "Deer" };
        frogRoot = new Node3D { Name = "Frogs" };
        AddChild(deerRoot);
        AddChild(frogRoot);
        RebuildDeer();
        RebuildFrogs();
    }

    public override void _Process(double delta)
    {
        if (herd == null) return;
        float dt = (float)delta;
        herd.Advance(dt);
        chorus.Advance(dt);
        for (int i = 0; i < deer.Count && i < herd.Total; i++) PoseDeer(deer[i], herd.Agents[i], dt);
        for (int i = 0; i < frogs.Count && i < chorus.Total; i++) PoseFrog(frogs[i], throats[i], chorus.Frogs[i]);
    }

    public override void _ExitTree()
    {
        if (herd == null) return;
        herd.Respawned -= RebuildDeer;
        chorus.Respawned -= RebuildFrogs;
    }

    /// <summary>R reset: fresh herd, fresh chorus.</summary>
    public void ResetAll()
    {
        herd?.ResetAll();
        chorus?.ResetAll();
    }

    private void PoseDeer(DeerBody body, DeerAgent agent, float delta)
    {
        Node3D figure = body.Figure;
        if (agent.IsDead)
        {
            if (agent.Death == DeerDeath.Drowned)
            {
                figure.Position = new Vector3(figure.Position.X, figure.Position.Y - 0.7f * delta, figure.Position.Z);
                if (agent.DeathTimer > 3f) figure.Visible = false;
            }
            else
            {
                Char(figure);
                figure.Rotation = new Vector3(
                    Mathf.LerpAngle(figure.Rotation.X, Mathf.Pi * .5f, 5f * delta), figure.Rotation.Y, 0f);
                if (agent.DeathTimer > 5f) figure.Visible = false;
            }
            return;
        }

        figure.Position = WorldCoordinates.ToGodot(agent.Position.X, agent.Position.Y, agent.Position.Z);
        // Reflecting Z at the engine boundary mirrors yaw; +PI puts Godot's -Z forward on the heading.
        figure.Rotation = new Vector3(0f, Mathf.Pi - agent.Heading, 0f);

        // Legs: diagonal pairs swing against the walk phase; amplitude follows the gait.
        float amp = Mathf.Clamp(agent.Speed * .13f, .02f, .62f) * Mathf.Max(0f, 1f - agent.GrazeAmount);
        for (int leg = 0; leg < body.Legs.Count; leg++)
        {
            float phase = agent.WalkPhase + (leg % 2 == 0 ? 0f : Mathf.Pi) + (leg / 2) * Mathf.Pi * .5f;
            body.Legs[leg].Rotation = new Vector3(Mathf.Sin(phase) * amp, 0f, 0f);
        }

        // Neck: alert on the move, down in the grass while grazing.
        body.Neck.Rotation = new Vector3(Mathf.LerpAngle(-0.55f, 0.85f, agent.GrazeAmount), 0f, 0f);
    }

    private void PoseFrog(Node3D figure, Node3D throat, FrogAgent agent)
    {
        if (agent.IsDead)
        {
            Char(figure);
            var squash = figure.Scale with { Y = .55f };
            figure.Scale = squash;
            if (agent.DeathTimer > 4f) figure.Visible = false;
            return;
        }

        float hop = Mathf.Sin(Mathf.Pi * agent.AirProgress) * FrogAgent.HopHeight;
        var at = WorldCoordinates.ToGodot(agent.Position.X, agent.Position.Y, agent.Position.Z);
        figure.Position = at with { Y = at.Y + .02f + hop };
        figure.Rotation = new Vector3(0f, Mathf.Pi - agent.Heading, 0f);
        figure.Visible = agent.State != FrogState.Submerged;
        throat.Scale = Vector3.One * (1f + agent.CroakPulse * .85f);
    }

    private void RebuildDeer()
    {
        foreach (DeerBody body in deer) body.Figure.QueueFree();
        deer.Clear();
        var rng = new Random(3312);
        for (int i = 0; i < herd.Total; i++)
        {
            DeerBody body = BuildDeer(rng, i);
            deerRoot.AddChild(body.Figure);
            deer.Add(body);
        }
    }

    private void RebuildFrogs()
    {
        foreach (Node3D frog in frogs) frog.QueueFree();
        frogs.Clear();
        throats.Clear();
        var rng = new Random(9021);
        for (int i = 0; i < chorus.Total; i++)
        {
            Node3D frog = BuildFrog(rng);
            frogRoot.AddChild(frog);
            frogs.Add(frog);
            throats.Add(frog.GetNode<Node3D>("Throat"));
        }
    }

    private DeerBody BuildDeer(Random rng, int index)
    {
        var figure = new Node3D { Name = $"Deer {index}" };
        bool stag = index == 0;
        Color coat = stag
            ? new Color(.52f, .40f, .28f)
            : new Color(.66f, .54f, .38f).Lerp(new Color(.58f, .50f, .36f), (float)rng.NextDouble());

        // Torso lies along -Z (forward after the yaw mirror): capsule rotated onto its side.
        var torso = Part("Torso", new CapsuleMesh { Radius = .21f, Height = .95f },
            new Vector3(0f, .68f, 0f), coat, .35f);
        torso.RotationDegrees = new Vector3(90f, 0f, 0f);
        figure.AddChild(torso);

        var neck = new Node3D { Name = "Neck", Position = new Vector3(0f, .84f, .40f) };
        var neckMesh = Part("Neck", new CylinderMesh { TopRadius = .055f, BottomRadius = .08f, Height = .42f },
            new Vector3(0f, .16f, .05f), coat, .35f);
        neckMesh.RotationDegrees = new Vector3(-38f, 0f, 0f);
        neck.AddChild(neckMesh);
        neck.AddChild(Part("Head", new BoxMesh { Size = new Vector3(.13f, .14f, .30f) },
            new Vector3(0f, .32f, .17f), coat * 1.05f, .35f));
        neck.AddChild(Part("Nose", new BoxMesh { Size = new Vector3(.09f, .09f, .10f) },
            new Vector3(0f, .29f, .33f), new Color(.25f, .18f, .14f), .2f));
        if (stag)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                var antler = Part("Antler", new CylinderMesh { TopRadius = .012f, BottomRadius = .02f, Height = .34f },
                    new Vector3(.055f * side, .52f, .13f), new Color(.55f, .45f, .34f), .1f);
                antler.RotationDegrees = new Vector3(-14f, 0f, 30f * side);
                neck.AddChild(antler);
            }
        }
        figure.AddChild(neck);

        var legs = new List<Node3D>();
        foreach (float side in new[] { -1f, 1f })
        foreach (float fore in new[] { 1f, -1f })
        {
            var hip = new Node3D { Name = $"Leg {side}{fore}", Position = new Vector3(.13f * side, .58f, .32f * fore) };
            hip.AddChild(Part("Shin", new CylinderMesh { TopRadius = .034f, BottomRadius = .026f, Height = .58f },
                new Vector3(0f, -.29f, 0f), coat * .9f, .3f));
            figure.AddChild(hip);
            legs.Add(hip);
        }

        var tail = Part("Tail", new BoxMesh { Size = new Vector3(.07f, .15f, .05f) },
            new Vector3(0f, .74f, -.52f), coat * 1.15f, .3f);
        tail.RotationDegrees = new Vector3(28f, 0f, 0f);
        figure.AddChild(tail);

        return new DeerBody { Figure = figure, Neck = neck, Legs = legs };
    }

    private Node3D BuildFrog(Random rng)
    {
        var figure = new Node3D { Name = "Frog" };
        Color skin = new Color(.30f, .44f, .16f).Lerp(new Color(.22f, .40f, .20f), (float)rng.NextDouble());

        figure.AddChild(Part("Body", new SphereMesh { Radius = .085f, Height = .11f, RadialSegments = 7, Rings = 2 },
            new Vector3(0f, .055f, 0f), skin, .3f));
        figure.AddChild(Part("Head", new SphereMesh { Radius = .055f, Height = .08f, RadialSegments = 7, Rings = 2 },
            new Vector3(0f, .09f, .065f), skin, .3f));
        var throat = new Node3D { Name = "Throat", Position = new Vector3(0f, .062f, .10f) };
        throat.AddChild(Part("Throat Sac", new SphereMesh { Radius = .04f, Height = .06f, RadialSegments = 7, Rings = 2 },
            Vector3.Zero, new Color(.72f, .82f, .42f), .3f));
        figure.AddChild(throat);
        foreach (float side in new[] { -1f, 1f })
            figure.AddChild(Part("Eye", new SphereMesh { Radius = .013f, Height = .026f, RadialSegments = 5, Rings = 2 },
                new Vector3(.03f * side, .125f, .085f), new Color(.05f, .05f, .04f), .5f));
        foreach (float side in new[] { -1f, 1f })
        {
            var haunch = Part("Haunch", new CapsuleMesh { Radius = .022f, Height = .10f },
                new Vector3(.05f * side, .045f, -.055f), skin * .92f, .3f);
            haunch.RotationDegrees = new Vector3(-34f, 0f, 24f * side);
            figure.AddChild(haunch);
        }
        return figure;
    }

    private static MeshInstance3D Part(string name, Mesh mesh, Vector3 at, Color colour, float smoothness)
        => new() { Name = name, Mesh = mesh, Position = at, MaterialOverride = SharedMaterial(colour, smoothness) };

    private static void Char(Node3D figure)
    {
        if (figure.HasMeta("charred")) return;
        figure.SetMeta("charred", true);
        foreach (Node child in figure.GetChildren())
        {
            if (child is MeshInstance3D part && part.MaterialOverride is StandardMaterial3D material)
                material.AlbedoColor = CharredColour;
            if (child is Node3D pivot)
                foreach (Node grandchild in pivot.GetChildren())
                    if (grandchild is MeshInstance3D limb && limb.MaterialOverride is StandardMaterial3D limbMaterial)
                        limbMaterial.AlbedoColor = CharredColour;
        }
    }

    private static StandardMaterial3D SharedMaterial(Color colour, float smoothness)
        => new() { AlbedoColor = colour, Roughness = 1f - smoothness, Metallic = 0f };

    private sealed class DeerBody
    {
        public Node3D Figure;
        public Node3D Neck;
        public List<Node3D> Legs;
    }
}
