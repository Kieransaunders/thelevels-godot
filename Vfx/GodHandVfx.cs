using Godot;
using System;
using TheLevels.Core.Simulation;
using TheLevels.Player;
using TheLevels.Simulation;

namespace TheLevels.Vfx;

/// <summary>
/// The god hand (P6): a golden pointer hovering over the brush point, a white
/// spirit mote trail laid down as the hand sweeps, and the carried matter orbiting
/// the hand. The orbit's density reads the actual selected-buffer fill ratio —
/// clamped, zero-capacity safe, and hidden while the hand is empty or lightning
/// is held — so the fill reads clearly without obscuring the terrain.
/// </summary>
public partial class GodHandVfx : Node3D
{
    private const float PointerHover = 1.2f;
    private const float OrbitHover = .95f;

    private static readonly Texture2D StarTexture = GD.Load<Texture2D>("res://Assets/Vfx/textures/spark_star.png");
    private static readonly Texture2D GlowTexture = GD.Load<Texture2D>("res://Assets/Vfx/textures/glow_soft.png");

    private WorldCursor cursor = null!;
    private HeightfieldSimulation simulation = null!;
    private FireSimulation fire = null!;

    private MeshInstance3D pointer = null!;
    private OmniLight3D pointerLight = null!;
    private GpuParticles3D motes = null!;
    private GpuParticles3D orbit = null!;
    private ParticleProcessMaterial earthOrbit = null!, waterOrbit = null!, emberOrbit = null!;
    private (MatterTool tool, MatterType matter) orbitKey;
    private double age;
    private bool debugDriven;

    public void Initialize(WorldCursor worldCursor, SimulationHost host)
    {
        cursor = worldCursor;
        simulation = host.Heightfield;
        fire = host.Fire;
        ProcessPriority = 1; // after the cursor, so the hand follows this frame's brush point

        var starMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            AlbedoTexture = StarTexture,
            AlbedoColor = new Color(1f, .84f, .45f, .95f)
        };
        pointer = new MeshInstance3D
        {
            Name = "GoldenPointer",
            Mesh = new QuadMesh { Size = new Vector2(.66f, .66f), Material = starMaterial },
            Position = new Vector3(0f, PointerHover, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(pointer);

        pointerLight = new OmniLight3D
        {
            Name = "HandLight",
            LightColor = new Color(1f, .78f, .42f), LightEnergy = .9f, OmniRange = 5.5f,
            ShadowEnabled = false, Position = new Vector3(0f, 1f, 0f)
        };
        AddChild(pointerLight);

        motes = new GpuParticles3D
        {
            Name = "SpiritTrail",
            Amount = 44, Lifetime = .5f,
            ProcessMaterial = TrailMotes(),
            DrawPass1 = GlowQuad(.26f),
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, 1.05f, 0f),
            VisibilityAabb = new Aabb(new Vector3(-8, -8, -8), new Vector3(16, 16, 16)),
            Emitting = false
        };
        AddChild(motes);

        orbit = new GpuParticles3D
        {
            Name = "CarriedMatter",
            Amount = 42, Lifetime = 1.1f,
            DrawPass1 = GlowQuad(.34f),
            // Local space keeps the orbital pivot at the hand: particles hug the hand
            // and the swirl translates with it.
            LocalCoords = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, OrbitHover, 0f),
            VisibilityAabb = new Aabb(new Vector3(-8, -8, -8), new Vector3(16, 16, 16)),
            Emitting = false
        };
        earthOrbit = OrbitMaterial(new Color(1f, .72f, .32f));
        waterOrbit = OrbitMaterial(new Color(.45f, .8f, 1f));
        emberOrbit = OrbitMaterial(new Color(1f, .52f, .16f));
        orbit.ProcessMaterial = earthOrbit;
        AddChild(orbit);
    }

    public override void _Process(double delta)
    {
        if (debugDriven || cursor == null) return;
        Tick((float)delta, cursor.HasTarget, cursor.CursorPosition);
    }

    /// <summary>
    /// One frame of hand presentation. Split from _Process so verification can drive
    /// it with an explicit target instead of the headless mouse position.
    /// </summary>
    public void Tick(float dt, bool hasTarget, Vector3 at)
    {
        age += dt;
        Visible = hasTarget;
        if (!hasTarget)
        {
            motes.Emitting = false;
            orbit.Emitting = false;
            return;
        }
        Position = at;
        float pulse = .84f + .16f * MathF.Sin((float)age * 5.1f);
        pointer.Scale = Vector3.One * pulse;
        pointerLight.LightEnergy = .9f * pulse;
        motes.Emitting = true;
        UpdateOrbit();
    }

    /// <summary>Pins the hand for staged captures; _Process stops overriding it.</summary>
    public void DebugDrive(bool hasTarget, Vector3 at)
    {
        debugDriven = true;
        Tick(1f / 60f, hasTarget, at);
    }

    /// <summary>Buffer fill ratio clamped into 0..1; zero capacity means nothing to show.</summary>
    internal static float ClampRatio(float buffer, float capacity)
        => capacity <= 0f ? 0f : Math.Clamp(buffer / capacity, 0f, 1f);

    // Verification read-outs for the P6 adapter gate.
    internal bool HandVisible => Visible;
    internal bool MotesEmitting => motes.Emitting;
    internal bool OrbitEmitting => orbit.Emitting;
    internal float OrbitRatio => orbit.AmountRatio;

    private void UpdateOrbit()
    {
        var key = (tool: cursor.SelectedTool,
            matter: cursor.SelectedTool == MatterTool.Matter ? cursor.CarriedMatter : MatterType.Earth);
        float ratio = ClampRatio(SelectedBuffer(), SelectedCapacity());
        bool show = cursor.SelectedTool != MatterTool.Lightning && ratio > .001f;
        orbit.Emitting = show;
        if (!show) return;
        // Density carries the reading: a sliver of buffer still shows the material,
        // a full one swirls dense enough to notice at gameplay distance.
        orbit.AmountRatio = .12f + .88f * ratio;
        if (key != orbitKey)
        {
            orbitKey = key;
            orbit.ProcessMaterial = cursor.SelectedTool == MatterTool.Fire ? emberOrbit
                : key.matter == MatterType.Water ? waterOrbit : earthOrbit;
        }
    }

    private float SelectedBuffer() => cursor.SelectedTool switch
    {
        MatterTool.Fire => fire.EmberBuffer,
        _ when cursor.CarriedMatter == MatterType.Water => simulation.WaterBuffer,
        _ => simulation.EarthBuffer
    };

    private float SelectedCapacity() => cursor.SelectedTool switch
    {
        MatterTool.Fire => fire.EmberCapacity,
        _ when cursor.CarriedMatter == MatterType.Water => simulation.WaterCapacity,
        _ => simulation.EarthCapacity
    };

    /// <summary>World-space motes with almost no velocity: the moving hand strings them out.</summary>
    private static ParticleProcessMaterial TrailMotes()
    {
        var ramp = new Gradient
        {
            Colors = new[] { new Color(1f, 1f, 1f, .85f), new Color(.92f, .96f, 1f, .35f), new Color(.9f, .95f, 1f, 0f) },
            Offsets = new[] { 0f, .45f, 1f }
        };
        return new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = .13f,
            Direction = new Vector3(0, 1, 0), Spread = 180f,
            InitialVelocityMin = .04f, InitialVelocityMax = .16f,
            Gravity = Vector3.Zero,
            DampingMin = .8f, DampingMax = 1.6f,
            ScaleMin = .7f, ScaleMax = 1.1f,
            ColorRamp = new GradientTexture1D { Gradient = ramp }
        };
    }

    /// <summary>Ring emission spun around the hand: orbital velocity does the swirling.</summary>
    private static ParticleProcessMaterial OrbitMaterial(Color core)
    {
        var ramp = new Gradient
        {
            Colors = new[] { new Color(core, .95f), new Color(core, .55f), new Color(core, 0f) },
            Offsets = new[] { 0f, .6f, 1f }
        };
        return new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = .5f,
            EmissionRingInnerRadius = .32f,
            EmissionRingHeight = .16f,
            OrbitVelocityMin = 2.3f,
            OrbitVelocityMax = 3.5f,
            Gravity = Vector3.Zero,
            ScaleMin = .8f, ScaleMax = 1.2f,
            ColorRamp = new GradientTexture1D { Gradient = ramp }
        };
    }

    private static QuadMesh GlowQuad(float size)
        => new() { Size = new Vector2(size, size), Material = SpellGlow() };

    private static StandardMaterial3D SpellGlow() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        BillboardKeepScale = true,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = GlowTexture,
        DisableReceiveShadows = true
    };
}
