using Godot;
using System;
using System.Collections.Generic;
using TheLevels.Core.Simulation;
using TheLevels.Simulation;
using TheLevels.View;
using TheLevels.Vfx;

namespace TheLevels.Player;

public enum MatterTool
{
    Matter,
    Fire,
    Lightning
}

/// <summary>
/// Port of WorldCursorController, with the matter hand reworked to From Dust rules:
/// the hand auto-selects whatever lies under the brush (water over wet cells, earth
/// over dry) and carries it; right click pours the carried matter. Fire and lightning
/// stay explicit tools on keys 3 and 4, with 1/2 returning to the matter hand.
/// </summary>
public partial class WorldCursor : Node3D
{
    /// <summary>R reset, for listeners that own state outside the two simulations.</summary>
    public event System.Action WorldReset;

    private SimulationHost host;
    private StrategyCamera camera;
    private HeightfieldSimulation simulation;
    private FireSimulation fire;

    private MeshInstance3D ring;
    private ImmediateMesh ringMesh;
    private ShaderMaterial ringMaterial;

    /// <summary>Native P6 tool effects; null keeps the cursor testable without visuals.</summary>
    public SpellVfx Spell { get; set; }

    private Vector3 cursorPosition;
    private bool hasTarget;
    private float strikeCooldownRemaining;
    private MatterType carriedMatter = MatterType.Earth;
    // Set when a held brush transferred nothing this frame (buffer full, or nothing carried).
    private bool brushIdle;

    private const float StrikeCooldown = 1.4f;
    private const int RingSegments = 64;
    private const float RingLift = 0.16f;
    // Wetness above this reads as open water: the hand scoops water there, earth below it.
    private const float WetThreshold = 0.02f;

    private static readonly Color EarthRing = new(1f, 0.68f, 0.20f, 0.95f);
    private static readonly Color WaterRing = new(0.35f, 0.78f, 1f, 0.95f);
    private static readonly Color FireRing = new(1f, 0.32f, 0.10f, 0.95f);
    private static readonly Color LightningRing = new(0.88f, 0.82f, 1f, 0.95f);

    public MatterTool SelectedTool { get; private set; } = MatterTool.Matter;

    /// <summary>Select the active tool programmatically (mirrors the 1–4 keys).</summary>
    public void SelectTool(MatterTool tool) => SelectedTool = host?.Mission != null ? MatterTool.Matter : tool;
    public MatterType CarriedMatter => carriedMatter;

    /// <summary>What the hand would scoop at this point: water over open water, earth on land.</summary>
    public MatterType MatterFor(System.Numerics.Vector3 simPoint) =>
        simulation != null && simulation.SampleWater(simPoint) > WetThreshold ? MatterType.Water : MatterType.Earth;

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
        {
            if (host.Mission == null) camera.Focus(cursorPosition);
            else
            {
                var centre = System.Numerics.Vector3.Zero;
                int count = 0;
                foreach (var person in host.Mission.Band.Agents)
                    if (!person.IsDead) { centre += person.Position; count++; }
                if (count > 0)
                {
                    centre /= count;
                    camera.Focus(WorldCoordinates.ToGodot(centre.X, centre.Y, centre.Z));
                }
            }
        }

        ringMaterial.SetShaderParameter("albedo", RingColor());
        UpdateRing();

        bool scooping = Input.IsActionPressed(InputBindings.Scoop);
        bool dropping = Input.IsActionPressed(InputBindings.Drop);
        var simPoint = WorldCoordinates.ToSimulation(cursorPosition);

        brushIdle = false;
        switch (SelectedTool)
        {
            case MatterTool.Matter:
            {
                var matter = MatterFor(simPoint);
                if (scooping)
                {
                    float moved = simulation.ApplyBrush(simPoint, matter, true, dt);
                    if (moved > 0f)
                    {
                        carriedMatter = matter;
                        Spell?.MatterScooped(cursorPosition, matter);
                    }
                    brushIdle = moved <= 0f;
                }
                if (dropping)
                {
                    float poured = simulation.ApplyBrush(simPoint, carriedMatter, false, dt);
                    brushIdle = poured <= 0f;
                    if (poured > 0f) Spell?.MatterDropped(cursorPosition, carriedMatter);
                }
                break;
            }
            case MatterTool.Fire:
            {
                if (scooping)
                {
                    float gathered = fire.ApplyFireBrush(simPoint, true, dt);
                    if (gathered > 0f) Spell?.EmberGathered(cursorPosition);
                }
                if (dropping)
                {
                    float kindled = fire.ApplyFireBrush(simPoint, false, dt);
                    if (kindled > 0f) Spell?.EmberDropped(cursorPosition);
                }
                break;
            }
            case MatterTool.Lightning:
                if (scooping && strikeCooldownRemaining <= 0f)
                    StrikeAt(simPoint);
                break;
        }
    }

    public override void _ExitTree()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Unity's Update key handling, expressed through Input Map actions.</summary>
    private void HandleGlobalControls()
    {
        // The hand is matter-agnostic now; 1/2 both just return to it from fire/lightning.
        if (Input.IsActionJustPressed(InputBindings.ToolEarth)) SelectedTool = MatterTool.Matter;
        if (Input.IsActionJustPressed(InputBindings.ToolWater)) SelectedTool = MatterTool.Matter;
        if (Input.IsActionJustPressed(InputBindings.ToolFire)) SelectTool(MatterTool.Fire);
        if (Input.IsActionJustPressed(InputBindings.ToolLightning)) SelectTool(MatterTool.Lightning);

        if (Input.IsActionJustPressed(InputBindings.ResetWorld))
        {
            simulation.ResetSimulation();
            fire.ResetFire();
            camera.ResetView();
            carriedMatter = MatterType.Earth;
            SelectedTool = MatterTool.Matter;
            strikeCooldownRemaining = 0f;
            WorldReset?.Invoke();
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
        Spell?.LightningStrike(WorldCoordinates.ToGodot(simPoint.X, 0f, simPoint.Z));
        GD.Print(struck > 0
            ? $"Lightning kindles {struck} cells of the reeds."
            : "Lightning hisses out on the wet ground.");
        return struck;
    }

    public bool LightningReady => strikeCooldownRemaining <= 0f;

    /// <summary>Cooldown-only tick for headless verification (Update also does this).</summary>
    public void TickCooldown(float seconds) => strikeCooldownRemaining -= seconds;

    private Color RingColor()
    {
        // A held brush that moves nothing (buffer full, nothing carried to pour)
        // looks identical to a broken click otherwise.
        if (brushIdle) return new Color(.55f, .55f, .52f, .75f);

        if (SelectedTool == MatterTool.Lightning)
        {
            float pulse = 0.7f + 0.3f * MathF.Sin((float)Time.GetUnixTimeFromSystem() * 9f);
            return new Color(LightningRing.R * pulse, LightningRing.G * pulse, LightningRing.B * pulse, 0.95f);
        }
        if (SelectedTool == MatterTool.Fire) return FireRing;
        // Hovering shows what a scoop would take; while pouring, show what will come out.
        var shown = Input.IsActionPressed(InputBindings.Drop) ? carriedMatter : MatterFor(WorldCoordinates.ToSimulation(cursorPosition));
        return shown == MatterType.Water ? WaterRing : EarthRing;
    }

    /// <summary>
    /// Unity TryFindSurface: cast a screen ray, start at plane y=1, then five refinements
    /// against the tool-appropriate surface (the water surface wherever the ground is wet,
    /// terrain otherwise; fire always aims at terrain). Fails outside the domain or for
    /// horizontal rays.
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
        bool fireTargetsTerrain = SelectedTool == MatterTool.Fire;
        for (int i = 0; i < 5; i++)
        {
            var simPoint = WorldCoordinates.ToSimulation(point);
            if (!simulation.ContainsWorldPosition(simPoint))
                return false;
            float surface = !fireTargetsTerrain && simulation.SampleWater(simPoint) > WetThreshold
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
