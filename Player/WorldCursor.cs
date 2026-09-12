using Godot;
using System;
using System.Collections.Generic;
using TheLevels.Core.Simulation;
using TheLevels.Simulation;
using TheLevels.View;

namespace TheLevels.Player;

public enum MatterTool
{
    Earth,
    Water,
    Fire,
    Lightning
}

/// <summary>
/// Port of WorldCursorController: tool selection (1–4), surface targeting with five
/// refinement passes (earth/fire target terrain; water/lightning target the water
/// surface when wet), scoop/drop brushes, lightning cooldown and the R/Space/N/F
/// global controls. The Unity LineRenderer pointer/god-hand visuals are P6; here the
/// terrain-following brush ring plus lightweight strike flashes carry the feedback.
/// </summary>
public partial class WorldCursor : Node3D
{
    private SimulationHost host;
    private StrategyCamera camera;
    private HeightfieldSimulation simulation;
    private FireSimulation fire;

    private MeshInstance3D ring;
    private ImmediateMesh ringMesh;
    private ShaderMaterial ringMaterial;
    private readonly List<(MeshInstance3D node, float ttl)> flashes = new();

    private Vector3 cursorPosition;
    private bool hasTarget;
    private float strikeCooldownRemaining;
    // Set when a held brush transferred nothing this frame (buffer full/empty, dry water).
    private bool brushIdle;

    private const float StrikeCooldown = 1.4f;
    private const int RingSegments = 64;
    private const float RingLift = 0.16f;
    private const float FlashLifetime = 0.35f;

    private static readonly Color EarthRing = new(1f, 0.68f, 0.20f, 0.95f);
    private static readonly Color WaterRing = new(0.35f, 0.78f, 1f, 0.95f);
    private static readonly Color FireRing = new(1f, 0.32f, 0.10f, 0.95f);
    private static readonly Color LightningRing = new(0.88f, 0.82f, 1f, 0.95f);

    public MatterTool SelectedTool { get; private set; } = MatterTool.Earth;

    /// <summary>Select the active tool programmatically (mirrors the 1–4 keys).</summary>
    public void SelectTool(MatterTool tool) => SelectedTool = tool;
    public MatterType SelectedMatter => SelectedTool == MatterTool.Water ? MatterType.Water : MatterType.Earth;
    public FireSimulation Fire => fire;
    public Vector3 CursorPosition => cursorPosition;
    public bool HasTarget => hasTarget;
    public float StrikeCooldownRemaining => strikeCooldownRemaining;

    public void Initialize(SimulationHost simulationHost, StrategyCamera strategyCamera)
    {
        host = simulationHost;
        camera = strategyCamera;
        simulation = host.Heightfield;
        fire = host.Fire;
        ProcessPriority = 0;
        CreateRing();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        strikeCooldownRemaining -= dt;

        HandleGlobalControls();

        Vector2 mouse = GetViewport().GetMousePosition();
        hasTarget = TryFindSurfaceAt(mouse, out cursorPosition);
        ring.Visible = hasTarget;
        Input.MouseMode = hasTarget ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;
        if (!hasTarget)
            return;

        if (Input.IsActionJustPressed(InputBindings.FocusCamera))
            camera.Focus(cursorPosition);

        ringMaterial.SetShaderParameter("albedo", RingColor());
        UpdateRing();

        bool scooping = Input.IsActionPressed(InputBindings.Scoop);
        bool dropping = Input.IsActionPressed(InputBindings.Drop);
        var simPoint = WorldCoordinates.ToSimulation(cursorPosition);

        brushIdle = false;
        switch (SelectedTool)
        {
            case MatterTool.Earth:
                if (scooping) brushIdle = simulation.ApplyBrush(simPoint, MatterType.Earth, true, dt) <= 0f;
                if (dropping) brushIdle = simulation.ApplyBrush(simPoint, MatterType.Earth, false, dt) <= 0f;
                break;
            case MatterTool.Water:
                if (scooping) brushIdle = simulation.ApplyBrush(simPoint, MatterType.Water, true, dt) <= 0f;
                if (dropping) brushIdle = simulation.ApplyBrush(simPoint, MatterType.Water, false, dt) <= 0f;
                break;
            case MatterTool.Fire:
                if (scooping) fire.ApplyFireBrush(simPoint, true, dt);
                if (dropping) fire.ApplyFireBrush(simPoint, false, dt);
                break;
            case MatterTool.Lightning:
                if (scooping && strikeCooldownRemaining <= 0f)
                    StrikeAt(simPoint);
                break;
        }

        UpdateFlashes(dt);
    }

    public override void _ExitTree()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Unity's Update key handling, expressed through Input Map actions.</summary>
    private void HandleGlobalControls()
    {
        if (Input.IsActionJustPressed(InputBindings.ToolEarth)) SelectedTool = MatterTool.Earth;
        if (Input.IsActionJustPressed(InputBindings.ToolWater)) SelectedTool = MatterTool.Water;
        if (Input.IsActionJustPressed(InputBindings.ToolFire)) SelectedTool = MatterTool.Fire;
        if (Input.IsActionJustPressed(InputBindings.ToolLightning)) SelectedTool = MatterTool.Lightning;

        if (Input.IsActionJustPressed(InputBindings.ResetWorld))
        {
            simulation.ResetSimulation();
            fire.ResetFire();
            camera.ResetView();
        }
        if (Input.IsActionJustPressed(InputBindings.TogglePause))
        {
            simulation.Paused = !simulation.Paused;
            fire.Paused = simulation.Paused;
        }
        if (Input.IsActionJustPressed(InputBindings.StepOnce) && simulation.Paused)
        {
            simulation.StepOnce();
            fire.StepOnce();
        }
    }

    /// <summary>Public for the P4 adapter gate: perform a lightning strike now.</summary>
    public int StrikeAt(System.Numerics.Vector3 simPoint)
    {
        strikeCooldownRemaining = StrikeCooldown;
        int struck = fire.Strike(simPoint, simulation.BrushRadius * 0.8f);
        // A bolt over open water boils a splash of it away.
        if (simulation.SampleWater(simPoint) > 0.05f)
            simulation.ApplyBrush(simPoint, MatterType.Water, true, 0.3f);
        SpawnStrikeFlash(WorldCoordinates.ToGodot(simPoint.X, 0, simPoint.Z));
        GD.Print(struck > 0
            ? $"Lightning kindles {struck} cells of the reeds."
            : "Lightning hisses out on the wet ground.");
        return struck;
    }

    public bool LightningReady => strikeCooldownRemaining <= 0f;

    /// <summary>Cooldown-only tick for headless verification (Update also does this).</summary>
    public void TickCooldown(float seconds) => strikeCooldownRemaining -= seconds;

    private void SpawnStrikeFlash(Vector3 worldPoint)
    {
        // Lightweight P4 feedback: a fading bolt column; polished VFX land in P6.
        var surface = simulation.SampleSurface(WorldCoordinates.ToSimulation(worldPoint));
        var mesh = new CylinderMesh { TopRadius = simulation.BrushRadius * 0.34f, BottomRadius = simulation.BrushRadius * 0.34f, Height = 26f };
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/unlit_color.gdshader") };
        material.SetShaderParameter("albedo", new Color(0.85f, 0.80f, 1f, 0.85f));
        mesh.Material = material;
        var node = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        node.Position = WorldCoordinates.ToGodot(worldPoint.X, surface + 13f, worldPoint.Z);
        AddChild(node);
        flashes.Add((node, FlashLifetime));
    }

    private void UpdateFlashes(float dt)
    {
        for (int i = flashes.Count - 1; i >= 0; i--)
        {
            var (node, ttl) = flashes[i];
            float next = ttl - dt;
            if (next <= 0f)
            {
                node.QueueFree();
                flashes.RemoveAt(i);
                continue;
            }
            if (node.Mesh is CylinderMesh cylinder && cylinder.Material is ShaderMaterial material)
                material.SetShaderParameter("albedo", new Color(0.85f, 0.80f, 1f, 0.85f * next / FlashLifetime));
            flashes[i] = (node, next);
        }
    }

    private Color RingColor()
    {
        // A held brush that moves nothing (buffer full or empty, dry ground under the
        // water tool) looks identical to a broken click otherwise.
        if (brushIdle) return new Color(.55f, .55f, .52f, .75f);

        if (SelectedTool == MatterTool.Lightning)
        {
            float pulse = 0.7f + 0.3f * MathF.Sin((float)Time.GetUnixTimeFromSystem() * 9f);
            return new Color(LightningRing.R * pulse, LightningRing.G * pulse, LightningRing.B * pulse, 0.95f);
        }
        return SelectedTool switch
        {
            MatterTool.Water => WaterRing,
            MatterTool.Fire => FireRing,
            _ => EarthRing
        };
    }

    /// <summary>
    /// Unity TryFindSurface: cast a screen ray, start at plane y=1, then five refinements
    /// against the tool-appropriate surface (water surface for water/lightning when wet,
    /// terrain otherwise). Fails outside the domain or for horizontal rays.
    /// </summary>
    public bool TryFindSurfaceAt(Vector2 screenPosition, out Vector3 point)
    {
        point = default;
        if (camera == null) return false;
        Vector3 origin = camera.ProjectRayOrigin(screenPosition);
        Vector3 direction = camera.ProjectRayNormal(screenPosition);
        if (MathF.Abs(direction.Y) < 0.0001f)
            return false;

        float t = (origin.Y - 1f) / -direction.Y;
        point = origin + direction * MathF.Max(0f, t);
        bool toWaterSurface = SelectedTool == MatterTool.Water || SelectedTool == MatterTool.Lightning;
        for (int i = 0; i < 5; i++)
        {
            var simPoint = WorldCoordinates.ToSimulation(point);
            if (!simulation.ContainsWorldPosition(simPoint))
                return false;
            float surface = toWaterSurface && simulation.SampleWater(simPoint) > 0.001f
                ? simulation.SampleSurface(simPoint)
                : simulation.SampleTerrain(simPoint);
            t = (origin.Y - surface) / -direction.Y;
            point = origin + direction * MathF.Max(0f, t);
        }
        return simulation.ContainsWorldPosition(WorldCoordinates.ToSimulation(point));
    }

    private void CreateRing()
    {
        ringMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/unlit_color.gdshader") };
        ringMaterial.SetShaderParameter("albedo", EarthRing);
        ringMesh = new ImmediateMesh();
        ring = new MeshInstance3D
        {
            Name = "AwenBrushRing",
            Mesh = ringMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(ring);
    }

    private void UpdateRing()
    {
        ringMesh.ClearSurfaces();
        ringMesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip, ringMaterial);
        for (int i = 0; i <= RingSegments; i++)
        {
            float angle = i / (float)RingSegments * MathF.Tau;
            var simSample = new System.Numerics.Vector3(
                WorldCoordinates.ToSimulation(cursorPosition).X + MathF.Cos(angle) * simulation.BrushRadius,
                0f,
                WorldCoordinates.ToSimulation(cursorPosition).Z + MathF.Sin(angle) * simulation.BrushRadius);
            float surface = simulation.SampleSurface(simSample);
            var godot = WorldCoordinates.ToGodot(simSample.X, surface + RingLift, simSample.Z);
            ringMesh.SurfaceAddVertex(godot);
        }
        ringMesh.SurfaceEnd();
    }
}
