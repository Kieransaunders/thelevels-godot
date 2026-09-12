using Godot;
using System;
using System.Collections.Generic;
using TheLevels.Core.Simulation;
using TheLevels.Simulation;
using TheLevels.View;

namespace TheLevels.Vfx;

/// <summary>
/// Native one-shot tool and ritual effects (P6): water/earth scoop and drop puffs,
/// ember gathering and kindling, the lightning bolt that replaces the P4 flash
/// cylinder, and the haven ritual burst — the port's first GPUParticles trails.
/// Built in code from Godot primitives plus the CC0 textures in Assets/Vfx (see
/// Assets/Vfx/LICENSE.md); nothing loads from reference/. Held-tool calls are
/// throttled so continuous scooping does not machine-gun bursts.
/// </summary>
public partial class SpellVfx : Node3D
{
    private static readonly Texture2D GlowTexture = GD.Load<Texture2D>("res://Assets/Vfx/textures/glow_soft.png");
    private static readonly Texture2D StarTexture = GD.Load<Texture2D>("res://Assets/Vfx/textures/spark_star.png");
    private static readonly Texture2D StreakTexture = GD.Load<Texture2D>("res://Assets/Vfx/textures/burst_streaks.png");

    private HeightfieldSimulation simulation = null!;
    private readonly List<Effect> effects = new();
    private readonly Dictionary<string, float> lastFired = new();
    private readonly Random random = new();

    private sealed class Effect
    {
        public Node3D Root = null!;
        public float Ttl;
        public float Fade;
        public Light3D Light;
        public float LightEnergy;
        public Material Fading;
        public Color FadingColour;
        public Action<Node3D, float> Animate;
        public float Elapsed;
    }

    public void Initialize(SimulationHost host) => simulation = host.Heightfield;

    /// <summary>Live transient effects; the P6 gate uses it to assert spawning/retirement.</summary>
    internal int TransientCount => effects.Count;

    public override void _Process(double delta)
    {
        if (ManualAge) return; // captures age effects deterministically via Tick
        Tick((float)delta);
    }

    /// <summary>Suspends wall-clock aging while staged captures step effects by hand.</summary>
    public bool ManualAge { get; set; }

    /// <summary>Retire every transient effect (world reset). Public for the reset path.</summary>
    public void ClearTransient()
    {
        foreach (var effect in effects) effect.Root.QueueFree();
        effects.Clear();
        lastFired.Clear();
    }

    /// <summary>Age and expire effects by an explicit number of seconds (verification).</summary>
    public void Tick(float seconds)
    {
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            var effect = effects[i];
            effect.Elapsed += seconds;
            effect.Ttl -= seconds;
            effect.Animate?.Invoke(effect.Root, effect.Elapsed);
            if (effect.Light != null) effect.Light.LightEnergy = effect.LightEnergy * MathF.Max(effect.Ttl / effect.Fade, 0f);
            if (effect.Fading != null && effect.Fading is StandardMaterial3D material)
                material.AlbedoColor = new Color(effect.FadingColour, effect.FadingColour.A * MathF.Max(effect.Ttl / effect.Fade, 0f));
            if (effect.Ttl <= 0f)
            {
                effect.Root.QueueFree();
                effects.RemoveAt(i);
            }
        }
    }

    public void MatterScooped(Vector3 at, MatterType matter)
    {
        if (matter == MatterType.Water) WaterScoop(at);
        else EarthScoop(at);
    }

    public void MatterDropped(Vector3 at, MatterType matter)
    {
        if (matter == MatterType.Water) WaterDrop(at);
        else EarthDrop(at);
    }

    public void EmberGathered(Vector3 at)
    {
        if (Throttled("ember_gather", 0.11f)) return;
        var root = Anchor(at, "Ember Gather");
        Spawn(root, Vector3.Zero, Process(new Color(1f, .58f, .16f, .95f), new Color(1f, .82f, .3f, 0f),
            new Vector3(0, 1, 0), 38f, .8f, 1.7f, new Vector3(0, .9f, 0), sphereRadius: .55f),
            StarTexture, .18f, amount: 14, lifetime: .6f);
        Retire(root, .8f);
    }

    public void EmberDropped(Vector3 at)
    {
        if (Throttled("ember_drop", 0.11f)) return;
        var root = Anchor(at, "Ember Drop");
        Spawn(root, Vector3.Zero, Process(new Color(1f, .45f, .1f, .95f), new Color(1f, .8f, .25f, 0f),
            new Vector3(0, 1, 0), 85f, 1.4f, 2.6f, new Vector3(0, -3f, 0), sphereRadius: .5f),
            StarTexture, .2f, amount: 20, lifetime: .55f);
        Retire(root, .8f);
    }

    private void WaterScoop(Vector3 at)
    {
        if (Throttled("water_scoop", 0.1f)) return;
        var root = Anchor(at, "Water Scoop");
        Spawn(root, Vector3.Zero, Process(new Color(.62f, .86f, .95f, .8f), new Color(.5f, .78f, .92f, 0f),
            new Vector3(0, 1, 0), 32f, .9f, 1.6f, new Vector3(0, -6.5f, 0), sphereRadius: .45f),
            GlowTexture, .14f, amount: 12, lifetime: .45f);
        Retire(root, .7f);
    }

    private void WaterDrop(Vector3 at)
    {
        if (Throttled("water_drop", 0.08f)) return;
        var root = Anchor(at, "Water Drop");
        Spawn(root, Vector3.Zero, Process(new Color(.66f, .88f, .97f, .9f), new Color(.45f, .75f, .9f, 0f),
            new Vector3(0, 1, 0), 58f, 2.1f, 3.4f, new Vector3(0, -9.8f, 0), sphereRadius: .5f),
            GlowTexture, .2f, amount: 24, lifetime: .6f);
        Retire(root, .8f);
    }

    private void EarthScoop(Vector3 at)
    {
        if (Throttled("earth_scoop", 0.12f)) return;
        var root = Anchor(at, "Earth Scoop");
        Spawn(root, Vector3.Zero, Process(new Color(.5f, .38f, .24f, .8f), new Color(.42f, .32f, .2f, 0f),
            new Vector3(0, 1, 0), 44f, .6f, 1.2f, new Vector3(0, -2.2f, 0), sphereRadius: .5f),
            GlowTexture, .3f, amount: 12, lifetime: .55f);
        Retire(root, .8f);
    }

    private void EarthDrop(Vector3 at)
    {
        if (Throttled("earth_drop", 0.1f)) return;
        var root = Anchor(at, "Earth Drop");
        Spawn(root, Vector3.Zero, Process(new Color(.58f, .45f, .3f, .85f), new Color(.45f, .35f, .22f, 0f),
            new Vector3(0, 1, 0), 62f, 1.1f, 2.0f, new Vector3(0, -4f, 0), sphereRadius: .55f),
            GlowTexture, .34f, amount: 18, lifetime: .6f);
        Retire(root, .8f);
    }

    /// <summary>
    /// Jagged bolt from the sky to a surface point, an impact burst and a flash of
    /// light. Replaces the P4 stand-in cylinder entirely (the old fade list is gone).
    /// </summary>
    public void LightningStrike(Vector3 impact)
    {
        var root = Anchor(impact, "Lightning");
        float surface = simulation.SampleSurface(WorldCoordinates.ToSimulation(impact));
        impact.Y = surface;

        var boltMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(.88f, .84f, 1f, .95f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        // Segments are children of the anchored root, so their coordinates are local:
        // the strike point is the origin and the sky sits 24 m above it.
        Vector3 sky = new(NextSigned(2.5f), 24f, NextSigned(2.5f));
        Vector3 previous = sky;
        const int segments = 5;
        for (int s = 1; s <= segments; s++)
        {
            float t = s / (float)segments;
            Vector3 point = sky.Lerp(Vector3.Zero, t)
                + new Vector3(NextSigned(1.2f * (1f - t)), 0f, NextSigned(1.2f * (1f - t)));
            AddBoltSegment(root, boltMaterial, previous, point, Mathf.Lerp(.06f, .18f, 1f - t));
            previous = point;
        }

        Spawn(root, Vector3.Zero, Process(new Color(.9f, .88f, 1f, .95f), new Color(.7f, .66f, 1f, 0f),
            new Vector3(0, 1, 0), 85f, 2.6f, 4.2f, new Vector3(0, -2f, 0), sphereRadius: .4f),
            StreakTexture, .5f, amount: 22, lifetime: .38f);
        var glowMaterial = GlowMaterial(StarTexture);
        glowMaterial.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        var glow = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2.2f, 2.2f), Material = glowMaterial },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        glow.Position = new Vector3(0f, .1f, 0f);
        root.AddChild(glow);

        var light = new OmniLight3D
        {
            LightColor = new Color(.86f, .83f, 1f), LightEnergy = 7f, OmniRange = 15f,
            ShadowEnabled = false, Position = new Vector3(0f, 2.2f, 0f)
        };
        root.AddChild(light);
        Retire(root, .38f, light, 7f, boltMaterial, new Color(.88f, .84f, 1f, .95f));
    }

    /// <summary>
    /// The haven ritual: golden spirit streaks rising from the seven stones (real
    /// GPUParticles tube trails — Y-to-velocity aligned so the tubes keep volume),
    /// a ground ring blooming outward and a warm light pulse.
    /// </summary>
    public void RitualBurst(Vector3 center, float radius)
    {
        // Anchor at the haven's ground level — the ring and light must sit on the
        // plain, not at Godot's Y=0 (the haven plain is ~5 m up on this map).
        float groundY = simulation.SampleTerrain(WorldCoordinates.ToSimulation(center));
        var root = new Node3D { Name = "Ritual" };
        root.Position = new Vector3(center.X, groundY, center.Z);
        AddChild(root);
        var ramp = new Gradient
        {
            Colors = new[] { new Color(1f, .84f, .42f, .95f), new Color(1f, .9f, .55f, .6f), new Color(1f, .95f, .7f, 0f) },
            Offsets = new[] { 0f, .5f, 1f }
        };
        var riseProcess = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = .25f,
            Direction = new Vector3(0, 1, 0), Spread = 7f,
            InitialVelocityMin = 1.6f, InitialVelocityMax = 2.3f,
            Gravity = Vector3.Zero,
            ScaleMin = 1f, ScaleMax = 1f,
            ColorRamp = new GradientTexture1D { Gradient = ramp }
        };
        var trailMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(1f, .86f, .45f, .9f),
            UseParticleTrails = true,
            DisableReceiveShadows = true
        };
        const int stones = 7;
        for (int s = 0; s < stones; s++)
        {
            float angle = s / (float)stones * Mathf.Pi * 2f;
            var at = new Vector3(MathF.Cos(angle) * 3.1f, 0f, MathF.Sin(angle) * 3.1f);
            float ground = simulation.SampleTerrain(WorldCoordinates.ToSimulation(center + at)) - groundY;
            var rise = new GpuParticles3D
            {
                Amount = 5, Lifetime = 1.7f, OneShot = true, Explosiveness = .8f,
                ProcessMaterial = riseProcess,
                TrailEnabled = true, TrailLifetime = .55f,
                TransformAlign = GpuParticles3D.TransformAlignEnum.YToVelocity,
                LocalCoords = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityAabb = new Aabb(new Vector3(-6, -1, -6), new Vector3(12, 14, 12))
            };
            var tube = new TubeTrailMesh
            {
                Radius = .09f, Sections = 6, SectionLength = .26f,
                CapTop = false, CapBottom = false, Curve = TaperCurve()
            };
            tube.Material = trailMaterial;
            rise.DrawPass1 = tube;
            rise.Position = new Vector3(at.X, ground + .5f, at.Z);
            root.AddChild(rise);
            rise.Emitting = true;
        }

        var ringMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(1f, .8f, .4f, .8f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = .9f, OuterRadius = 1f, Material = ringMaterial },
            Position = new Vector3(0f, .3f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        root.AddChild(ring);

        var light = new OmniLight3D
        {
            LightColor = new Color(1f, .8f, .45f), LightEnergy = 12f, OmniRange = 16f,
            ShadowEnabled = false, Position = new Vector3(0f, 2.5f, 0f)
        };
        root.AddChild(light);

        effects.Add(new Effect
        {
            Root = root, Ttl = 3.4f, Fade = 3.4f, Light = light, LightEnergy = 12f,
            Fading = ringMaterial, FadingColour = new Color(1f, .8f, .4f, .8f),
            Animate = (node, elapsed) =>
            {
                float t = Mathf.Clamp(elapsed / 1.4f, 0f, 1f);
                float scale = Mathf.Lerp(.6f, radius + 1.4f, Mathf.SmoothStep(0f, 1f, t));
                ring.Scale = new Vector3(scale, 1.6f, scale);
            }
        });
    }

    private Node3D Anchor(Vector3 at, string name)
    {
        var root = new Node3D { Name = name };
        root.Position = at;
        AddChild(root);
        return root;
    }

    private static void Spawn(Node3D root, Vector3 offset, ParticleProcessMaterial process,
        Texture2D texture, float quadSize, int amount, float lifetime, float explosiveness = 1f)
    {
        var quad = new QuadMesh { Size = new Vector2(quadSize, quadSize), Material = GlowMaterial(texture) };
        var burst = new GpuParticles3D
        {
            Amount = amount, Lifetime = lifetime, OneShot = true, Explosiveness = explosiveness,
            ProcessMaterial = process, DrawPass1 = quad,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-12, -12, -12), new Vector3(24, 24, 24)),
            Position = offset
        };
        root.AddChild(burst);
        burst.Emitting = true;
    }

    private void AddBoltSegment(Node3D root, Material material, Vector3 from, Vector3 to, float topRadius)
    {
        Vector3 span = to - from;
        var cylinder = new CylinderMesh
        {
            TopRadius = topRadius,
            BottomRadius = topRadius + .07f,
            Height = span.Length(),
            RadialSegments = 7,
            Material = material
        };
        root.AddChild(new MeshInstance3D
        {
            Mesh = cylinder,
            Transform = new Transform3D(AlongY(span), (from + to) * .5f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    private void Retire(Node3D root, float fade, Light3D light = null, float lightEnergy = 0f,
        Material fading = null, Color fadingColour = default)
    {
        effects.Add(new Effect
        {
            Root = root, Ttl = fade, Fade = fade, Light = light, LightEnergy = lightEnergy,
            Fading = fading, FadingColour = fadingColour
        });
    }

    /// <summary>Additive unshaded billboard material; particle colour feeds it as vertex colour.</summary>
    private static StandardMaterial3D GlowMaterial(Texture2D texture) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        BillboardKeepScale = true,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = texture,
        DisableReceiveShadows = true
    };

    private static ParticleProcessMaterial Process(Color start, Color end, Vector3 direction, float spread,
        float velocityMin, float velocityMax, Vector3? gravity = null,
        float scaleMin = .75f, float scaleMax = 1.25f, float sphereRadius = .5f)
    {
        var ramp = new Gradient
        {
            Colors = new[] { start, start, end },
            Offsets = new[] { 0f, .3f, 1f }
        };
        return new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = sphereRadius,
            Direction = direction,
            Spread = spread,
            InitialVelocityMin = velocityMin,
            InitialVelocityMax = velocityMax,
            Gravity = gravity ?? Vector3.Zero,
            ScaleMin = scaleMin,
            ScaleMax = scaleMax,
            ColorRamp = new GradientTexture1D { Gradient = ramp }
        };
    }

    private static Curve TaperCurve()
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, .9f));
        curve.AddPoint(new Vector2(.55f, .55f));
        curve.AddPoint(new Vector2(1f, 0f));
        return curve;
    }

    /// <summary>Basis whose local +Y runs along `direction` (bolt segments are Y-axis cylinders).</summary>
    private static Basis AlongY(Vector3 direction)
    {
        Vector3 y = direction.Normalized();
        Vector3 x = MathF.Abs(y.Y) < .99f ? Vector3.Up.Cross(y).Normalized() : Vector3.Right;
        Vector3 z = x.Cross(y).Normalized();
        return new Basis(x, y, z);
    }

    private float NextSigned(float amplitude) => ((float)random.NextDouble() * 2f - 1f) * amplitude;

    private bool Throttled(string key, float interval)
    {
        float now = Time.GetTicksMsec() / 1000f;
        if (lastFired.TryGetValue(key, out float when) && now - when < interval) return true;
        lastFired[key] = now;
        return false;
    }
}
