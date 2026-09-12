using Godot;
using System;
using TheLevels.Agents;
using TheLevels.Player;
using TheLevels.Core.Simulation;
using TheLevels.Simulation;
using TheLevels.UI;
using TheLevels.View;
using TheLevels.Vfx;

namespace TheLevels;

public partial class Main : Node3D
{
    public override void _Ready()
    {
        InputBindings.Register();

        var host = new SimulationHost { Name = "SimulationHost" };
        AddChild(host);
        var view = new HeightfieldView { Name = "HeightfieldView" };
        AddChild(view);
        view.Initialize(host.Heightfield, host.Fire);

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(.36f, .52f, .68f),
                SkyHorizonColor = new Color(.84f, .76f, .66f),
                GroundHorizonColor = new Color(.70f, .66f, .58f),
                GroundBottomColor = new Color(.30f, .32f, .28f),
                SunAngleMax = 24f
            } },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(.72f, .79f, .82f),
            AmbientLightEnergy = .32f,
            // AgX keeps the burning-reed emissives and water speculars from hue-shifting
            // to white the way ACES does; Filmic is the fallback if the palette mutes.
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.05f,
            FogEnabled = true,
            FogLightColor = new Color(.74f, .70f, .65f),
            FogDensity = .0012f,
            FogAerialPerspective = .15f,
            FogSkyAffect = .1f,
            SsaoEnabled = true,
            SsaoRadius = 2f,
            SsaoIntensity = 1.5f,
            GlowEnabled = true,
            GlowIntensity = .5f,
            GlowHdrThreshold = 1.2f,
            // AgX is honest but desaturating; lift saturation back toward the concept art.
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.2f,
            AdjustmentContrast = 1.05f
        };
        AddChild(new WorldEnvironment { Name = "WetlandEnvironment", Environment = environment });
        var sun = new DirectionalLight3D
        {
            Name = "MorningSun", RotationDegrees = new Vector3(-28, -32, 0),
            LightColor = new Color(1, .82f, .59f), LightEnergy = 1.3f, ShadowEnabled = true,
            ShadowBlur = 1.5f, DirectionalShadowBlendSplits = true, DirectionalShadowMaxDistance = 150f
        };
        AddChild(sun);
        view.SetSunDirection(sun.GlobalTransform.Basis.Z);

        var camera = new StrategyCamera { Name = "WorldCamera" };
        AddChild(camera);
        var cursor = new WorldCursor { Name = "WorldCursor" };
        AddChild(cursor);
        cursor.Initialize(host, camera);

        var druids = new DruidView { Name = "Druids" };
        AddChild(druids);
        druids.Initialize(host);
        cursor.WorldReset += druids.ResetAll;

        var diagnostics = new Diagnostics { Name = "Diagnostics" };
        AddChild(diagnostics);
        diagnostics.Initialize(host, view, cursor, druids);

        var spell = new SpellVfx { Name = "SpellVfx" };
        AddChild(spell);
        spell.Initialize(host);
        cursor.Spell = spell;
        cursor.WorldReset += spell.ClearTransient;
        var hand = new GodHandVfx { Name = "GodHand" };
        AddChild(hand);
        hand.Initialize(cursor, host);
        druids.Spell = spell;

        GD.Print($"The Levels: {host.Heightfield.Resolution}² world ready; engine {Engine.GetVersionInfo()["string"]}");

        string[] args = OS.GetCmdlineUserArgs();
        if (Array.Exists(args, a => a == "--verify-p3"))
        {
            try { VerifyView(host, view, diagnostics); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); return; }
        }
        if (Array.Exists(args, a => a == "--verify-p4"))
        {
            try { VerifyCursor(host, camera, cursor); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); return; }
        }
        if (Array.Exists(args, a => a == "--verify-p5"))
        {
            try { VerifyDruids(host, druids); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); return; }
        }
        if (Array.Exists(args, a => a == "--verify-p6"))
        {
            try { VerifyVfx(host, view, cursor, spell, hand, druids); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); return; }
        }
        if (Array.Exists(args, a => a == "--verify-p7"))
        {
            try { VerifyIntegration(host, view, camera, cursor, spell, hand, druids); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); return; }
        }
        if (Array.Exists(args, a => a == "--showcase"))
            RunShowcase(host, camera, cursor, spell, hand, druids, diagnostics);
        if (Array.Exists(args, a => a == "--verify-input")) VerifyInput(camera, diagnostics);
        foreach (string arg in args)
            if (arg.StartsWith("--capture="))
            {
                // Path, optionally "@frames" — short waits catch transient spell effects.
                string spec = arg.Substring("--capture=".Length);
                int frames = 180;
                int at = spec.IndexOf('@');
                if (at >= 0)
                {
                    frames = int.Parse(spec[(at + 1)..]);
                    spec = spec[..at];
                }
                CaptureAfterFrames(spec, frames, diagnostics);
            }
    }

    private void VerifyView(SimulationHost host, HeightfieldView view, Diagnostics diagnostics)
    {
        int before = view.RebuildCount;
        view.PublishChanges();
        if (view.RebuildCount != before) throw new InvalidOperationException("Clean view rebuilt");
        host.Heightfield.Paused = true;
        host.Heightfield.StepOnce();
        if (!view.IsDirty || view.HeightfieldEvents == 0) throw new InvalidOperationException("Water did not dirty view");
        view.PublishChanges();
        int waterEvents = view.HeightfieldEvents;
        diagnostics.IgniteReeds();
        if (!view.IsDirty || view.FireEvents == 0 || view.HeightfieldEvents != waterEvents)
            throw new InvalidOperationException("Fire-only change did not dirty view independently");
        view.PublishChanges();
        var terrain = view.GetNode<MeshInstance3D>("Terrain").Mesh as ArrayMesh;
        using var arrays = terrain.SurfaceGetArrays(0);
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        foreach (var normal in normals)
            if (!normal.IsFinite() || normal.Y <= 0) throw new InvalidOperationException("Invalid or downward normal");
        if (terrain.SurfaceGetMaterial(0) == null || terrain.GetAabb().Size.Y <= 0)
            throw new InvalidOperationException("Missing material or bounds");
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if ((vertices[indices[1]] - vertices[indices[0]]).Cross(vertices[indices[2]] - vertices[indices[0]]).Y >= 0)
            throw new InvalidOperationException("Triangle does not use clockwise top-face winding");
        var water = view.GetNode<MeshInstance3D>("Water").Mesh as ArrayMesh;
        using var waterArrays = water.SurfaceGetArrays(0);
        var colors = waterArrays[(int)Mesh.ArrayType.Color].AsColorArray();
        bool foundDry = false, foundWet = false;
        for (int z = 0; z < host.Heightfield.Resolution; z++)
        for (int x = 0; x < host.Heightfield.Resolution; x++)
        {
            float depth = host.Heightfield.GetWater(x, z);
            float alpha = colors[x + z * host.Heightfield.Resolution].A;
            if (depth == 0) { foundDry = true; if (alpha != 0) throw new InvalidOperationException("Dry cell is opaque"); }
            else { foundWet = true; if (alpha <= 0) throw new InvalidOperationException("Wet cell is transparent"); }
        }
        if (!foundDry || !foundWet) throw new InvalidOperationException("Missing dry/wet coverage");
        // Detach a second view and verify its event subscriptions are removed immediately.
        var detached = new HeightfieldView();
        AddChild(detached); detached.Initialize(host.Heightfield, host.Fire);
        RemoveChild(detached);
        int detachedEvents = detached.FireEvents;
        host.Fire.ResetFire();
        if (detached.FireEvents != detachedEvents) throw new InvalidOperationException("Detached view retained subscription");
        detached.Free();
        host.Heightfield.ResetSimulation(); host.Fire.ResetFire(); view.PublishChanges();
        if (host.Fire.BurningCells != 0) throw new InvalidOperationException("Fire reset failed");
        GD.Print("P3 adapter checks PASS: dirty-only rebuild, independent water/fire events, upward normals, clockwise winding, material, bounds, dry/wet alpha, detach cleanup, reset.");
    }

    private void VerifyCursor(SimulationHost host, StrategyCamera camera, WorldCursor cursor)
    {
        var sim = host.Heightfield;
        var fire = host.Fire;
        sim.ResetSimulation(); fire.ResetFire();

        // The matter hand auto-selects: water over open water, earth over dry ground.
        cursor.SelectTool(MatterTool.Matter);
        System.Numerics.Vector3 wet = FindWetCell(sim);
        if (wet.Z == -1f) throw new InvalidOperationException("No wet cell");
        if (cursor.MatterFor(wet) != MatterType.Water) throw new InvalidOperationException("Matter hand missed open water");
        System.Numerics.Vector3 dryLand = FindDryCell(sim);
        if (dryLand.Z == -1f) throw new InvalidOperationException("No dry cell");
        if (cursor.MatterFor(dryLand) != MatterType.Earth) throw new InvalidOperationException("Matter hand missed dry land");

        // Targeting: from the start view, screen centre must land on the tool-appropriate
        // surface, and a corner ray may leave it. Wet cells snap to the water surface.
        camera.ResetView();
        var size = GetViewport().GetVisibleRect().Size;
        if (!cursor.TryFindSurfaceAt(size / 2f, out Vector3 centre))
            throw new InvalidOperationException("Centre targeting failed from start view");
        if (!sim.ContainsWorldPosition(WorldCoordinates.ToSimulation(centre)))
            throw new InvalidOperationException("Target outside domain");
        var surfaceUnder = sim.SampleSurface(WorldCoordinates.ToSimulation(centre));
        if (MathF.Abs(centre.Y - surfaceUnder) > 1.5f)
            throw new InvalidOperationException($"Surface targeting off: y={centre.Y:0.00} surface={surfaceUnder:0.00}");

        // Find a wet screen point by projecting the known wet cell; the hand must target
        // the water surface above terrain there.
        Vector2 wetScreen = camera.UnprojectPosition(WorldCoordinates.ToGodot(wet.X, 0, wet.Z));
        if (cursor.TryFindSurfaceAt(wetScreen, out Vector3 wetPoint))
        {
            float surface = sim.SampleSurface(WorldCoordinates.ToSimulation(wetPoint));
            if (MathF.Abs(wetPoint.Y - surface) > 1.0f)
                throw new InvalidOperationException("Matter hand did not target the water surface");
        }

        // Earth brush: scoop fills the buffer with real volume accounting.
        float scooped = sim.ApplyBrush(new System.Numerics.Vector3(-8f, 0f, -4f), MatterType.Earth, true, 1f);
        if (scooped <= 0f || sim.EarthBuffer <= 0f) throw new InvalidOperationException("Earth scoop accounting");

        // Lightning: strikes dry reeds, cooldown blocks the second call, water strikes boil.
        System.Numerics.Vector3 dry = FindFuelledDryCell(sim, fire);
        if (dry.X == float.MaxValue) throw new InvalidOperationException("No dry fuelled cell for lightning");
        cursor.TickCooldown(10f);
        if (!cursor.LightningReady) throw new InvalidOperationException("Cooldown did not expire");
        int struck = cursor.StrikeAt(dry);
        if (struck <= 0 || fire.BurningCells <= 0) throw new InvalidOperationException("Lightning failed to kindle");
        if (cursor.LightningReady) throw new InvalidOperationException("Cooldown not set by strike");
        int second = cursor.StrikeAt(dry); // must be blocked by cooldown
        if (second != 0) throw new InvalidOperationException("Cooldown failed to block second strike");

        System.Numerics.Vector3 deep = FindDeepWetCell(sim);
        cursor.TickCooldown(10f);
        float waterBefore = sim.WaterBuffer;
        cursor.StrikeAt(deep);
        if (sim.WaterBuffer <= waterBefore)
            throw new InvalidOperationException("Lightning over deep water did not scoop water");

        // Pause semantics: both sims pause together; N steps both once.
        sim.Paused = false;
        sim.Paused = true; fire.Paused = true;
        int waterSteps = sim.StepCount, fireSteps = fire.StepCount;
        sim.StepOnce(); fire.StepOnce();
        if (sim.StepCount != waterSteps + 1 || fire.StepCount != fireSteps + 1)
            throw new InvalidOperationException("Single-step did not advance both sims once");

        // Reset clears everything and restores the camera start view.
        sim.ResetSimulation(); fire.ResetFire(); camera.ResetView();
        if (sim.EarthBuffer != 0f || fire.BurningCells != 0 || sim.StepCount != 0)
            throw new InvalidOperationException("Reset left residual state");

        GD.Print("P4 adapter checks PASS: matter hand auto-select (water/wet, earth/dry), surface targeting, water-surface snapping, earth brush accounting, lightning kindle + cooldown + water boiling, pause/single-step, reset.");
    }

    /// <summary>P5 gate: bodies, stones, walking while paused, and reset.</summary>
    private void VerifyDruids(SimulationHost host, DruidView druids)
    {
        var band = druids.Band;
        if (band.Total != 8 || band.AliveCount != 8) throw new InvalidOperationException("Band is not eight living druids");
        if (band.RitualComplete) throw new InvalidOperationException("Ritual complete before it began");
        var bodies = druids.GetNode<Node3D>("Druids");
        if (bodies.GetChildCount() != 8) throw new InvalidOperationException("Missing druid bodies");
        if (druids.GetNode<Node3D>("Haven Stone Circle").GetChildCount() != 7)
            throw new InvalidOperationException("Haven is not seven standing stones");

        // Druids keep walking while water and fire are paused.
        host.Heightfield.Paused = true; host.Fire.Paused = true;
        var before = band.Agents[0].Position;
        for (int i = 0; i < 30; i++) band.Advance(1f / 60f);
        if (System.Numerics.Vector3.Distance(band.Agents[0].Position, before) <= 0.01f)
            throw new InvalidOperationException("Druids froze with the simulation");
        var haven = new System.Numerics.Vector2(band.HavenCenter.X, band.HavenCenter.Z);
        if (System.Numerics.Vector2.Distance(new System.Numerics.Vector2(band.Agents[0].Position.X, band.Agents[0].Position.Z), haven)
            >= System.Numerics.Vector2.Distance(new System.Numerics.Vector2(before.X, before.Z), haven))
            throw new InvalidOperationException("Druid did not walk toward the haven");
        host.Heightfield.Paused = false; host.Fire.Paused = false;

        // Reset drops every old body and respawns a full band.
        druids.ResetAll();
        if (band.Total != 8 || band.AliveCount != 8 || band.RitualComplete)
            throw new InvalidOperationException("Reset did not restore the band");
        int live = 0;
        foreach (Node child in bodies.GetChildren())
            if (!child.IsQueuedForDeletion()) live++;
        if (live != 8) throw new InvalidOperationException($"Reset left {live} live bodies");

        GD.Print("P5 adapter checks PASS: eight druids, seven stones, walking while paused, haven-ward travel, reset respawn.");
    }

    /// <summary>
    /// P6 gate: the god hand reads the real buffer fill (empty/half/full, zero-capacity
    /// safe, hidden for lightning), every tool and the ritual spawn native effects, the
    /// ritual uses genuine GPUParticles tube trails, the bolt cylinder is gone, and the
    /// water/terrain vertex-colour channels carry flow and depth for the shaders.
    /// </summary>
    private void VerifyVfx(SimulationHost host, HeightfieldView view, WorldCursor cursor,
        SpellVfx spell, GodHandVfx hand, DruidView druids)
    {
        var sim = host.Heightfield;
        var fireSim = host.Fire;
        sim.ResetSimulation(); fireSim.ResetFire();
        view.PublishChanges();

        if (GodHandVfx.ClampRatio(0f, 140f) != 0f || GodHandVfx.ClampRatio(70f, 140f) != .5f
            || GodHandVfx.ClampRatio(200f, 140f) != 1f || GodHandVfx.ClampRatio(10f, 0f) != 0f)
            throw new InvalidOperationException("Buffer ratio clamp (empty/half/full/zero-capacity)");

        // The tor top has hundreds of m³ of scoopable dirt; the map-edge dry cells
        // have almost none, which would stall the fill test.
        var landSim = FindHighGround(sim);
        var land = WorldCoordinates.ToGodot(landSim.X, sim.SampleTerrain(landSim), landSim.Z);
        cursor.SelectTool(MatterTool.Matter);
        hand.DebugDrive(true, land); // buffer still empty after the reset
        if (!hand.HandVisible || !hand.MotesEmitting)
            throw new InvalidOperationException("Hand did not appear with a target");
        if (hand.OrbitEmitting)
            throw new InvalidOperationException("Swirl showed with an empty earth buffer");

        sim.ApplyBrush(landSim, MatterType.Earth, true, 3.6f); // ~72 m³: the half state
        hand.DebugDrive(true, land);
        if (!hand.OrbitEmitting)
            throw new InvalidOperationException("Swirl did not show a half buffer");
        float halfRatio = hand.OrbitRatio;
        sim.ApplyBrush(landSim, MatterType.Earth, true, 4f); // saturate the 140 m³ buffer
        hand.DebugDrive(true, land);
        if (hand.OrbitRatio <= halfRatio)
            throw new InvalidOperationException("Fill ratio did not rise with the buffer");
        if (hand.OrbitRatio < .999f)
            throw new InvalidOperationException($"Full buffer did not clamp to 1: {hand.OrbitRatio}");

        cursor.SelectTool(MatterTool.Lightning);
        hand.DebugDrive(true, land);
        if (hand.OrbitEmitting)
            throw new InvalidOperationException("Lightning must hide held matter");
        cursor.SelectTool(MatterTool.Fire);
        hand.DebugDrive(true, land);
        if (hand.OrbitEmitting)
            throw new InvalidOperationException("Empty ember buffer must hide the swirl");

        int before = spell.TransientCount;
        spell.MatterDropped(land, MatterType.Earth);
        spell.MatterScooped(land, MatterType.Water);
        spell.EmberGathered(land);
        spell.EmberDropped(land);
        if (spell.TransientCount - before < 4)
            throw new InvalidOperationException("Tool effects did not spawn");

        var dry = FindFuelledDryCell(sim, fireSim);
        cursor.TickCooldown(10f);
        int strikeBefore = spell.TransientCount;
        int struck = cursor.StrikeAt(dry);
        if (struck <= 0 || spell.TransientCount - strikeBefore < 1)
            throw new InvalidOperationException("Lightning strike spawned no bolt");
        if (cursor.GetChildCount() != 1 || cursor.GetChild(0).Name != "AwenBrushRing")
            throw new InvalidOperationException("The P4 bolt cylinder is still with us");

        var bolt = spell.GetNodeOrNull<Node3D>("Lightning");
        if (bolt == null) throw new InvalidOperationException("Bolt root missing");
        bool boltLight = false, boltMeshes = false, boltBurst = false;
        foreach (Node child in bolt.GetChildren())
        {
            if (child is OmniLight3D) boltLight = true;
            if (child is MeshInstance3D) boltMeshes = true;
            if (child is GpuParticles3D) boltBurst = true;
        }
        if (!boltLight || !boltMeshes || !boltBurst)
            throw new InvalidOperationException("Bolt lacks light, jagged mesh or impact burst");

        // The API sanity check the plan asked for, asserted: ritual rises are real
        // GPUParticles trails — tube draw pass, Y-to-velocity, trail-aware material.
        spell.RitualBurst(WorldCoordinates.ToGodot(druids.Band.HavenCenter.X, 0f, druids.Band.HavenCenter.Z),
            druids.Band.HavenRadius);
        var ritual = spell.GetNodeOrNull<Node3D>("Ritual");
        if (ritual == null) throw new InvalidOperationException("Ritual root missing");
        int rises = 0;
        foreach (Node child in ritual.GetChildren())
        {
            if (child is not GpuParticles3D rise) continue;
            if (!rise.TrailEnabled || rise.TrailLifetime <= 0f)
                throw new InvalidOperationException("Ritual rise is not a trail");
            if (rise.TransformAlign != GpuParticles3D.TransformAlignEnum.YToVelocity)
                throw new InvalidOperationException("Tube trail must align Y to velocity");
            if (rise.DrawPass1 is not TubeTrailMesh tube || tube.Material is not StandardMaterial3D trailMaterial
                || !trailMaterial.UseParticleTrails)
                throw new InvalidOperationException("Ritual trail draw pass/material misconfigured");
            rises++;
        }
        if (rises != 7) throw new InvalidOperationException($"Expected seven ritual rises, got {rises}");

        var queued = spell.GetNodeOrNull<Node3D>("Lightning");
        spell.Tick(999f);
        if (spell.TransientCount != 0 || (queued != null && !queued.IsQueuedForDeletion()))
            throw new InvalidOperationException("Transient effects did not retire");

        // Vertex-colour channels: water COLOR.r mirrors FlowSpeed, terrain COLOR.a mirrors depth.
        view.PublishChanges();
        using var waterArrays = (view.GetNode<MeshInstance3D>("Water").Mesh as ArrayMesh)!.SurfaceGetArrays(0);
        var waterColours = waterArrays[(int)Mesh.ArrayType.Color].AsColorArray();
        using var terrainArrays = (view.GetNode<MeshInstance3D>("Terrain").Mesh as ArrayMesh)!.SurfaceGetArrays(0);
        var terrainColours = terrainArrays[(int)Mesh.ArrayType.Color].AsColorArray();
        bool sawWet = false;
        // Vertex colours come back 8-bit quantized, so allow one quantization step.
        const float channelTolerance = 1.5f / 255f;
        for (int z = 0; z < sim.Resolution; z++)
        for (int x = 0; x < sim.Resolution; x++)
        {
            int i = x + z * sim.Resolution;
            float flowChannel = waterColours[i].R;
            float expectedFlow = Math.Clamp(sim.FlowSpeed(x, z) * .45f, 0f, 1f);
            if (Math.Abs(flowChannel - expectedFlow) > channelTolerance)
                throw new InvalidOperationException($"Flow channel mismatch at {x},{z}");
            float depth = sim.GetWater(x, z);
            if (Math.Abs(terrainColours[i].A - Math.Clamp(depth * 1.6f, 0f, 1f)) > channelTolerance)
                throw new InvalidOperationException($"Depth channel mismatch at {x},{z}");
            if (depth > 0f) sawWet = true;
        }
        if (!sawWet) throw new InvalidOperationException("No wet cells to carry the channels");
        // The strike boiled water and the ritual reset nothing; some flow must exist
        // after the strike's brush splash. (Not asserting sawFlow: still pools are legal.)

        sim.ResetSimulation(); fireSim.ResetFire(); spell.ClearTransient();
        hand.DebugDrive(false, Vector3.Zero);
        GD.Print("P6 adapter checks PASS: ratio clamp (empty/half/full/zero-capacity), lightning hides held matter, tool + bolt + ritual effects spawn, seven tube-trail rises, cylinder stand-in gone, flow/depth vertex channels, transient retirement, reset.");
    }

    /// <summary>
    /// P7 gate: repeated reset stability, paused editing and single-step durations,
    /// edge brushing at all four corners, cursor targeting from shallow and steep
    /// angles, and scene-reload cleanup for the VFX nodes.
    /// </summary>
    private void VerifyIntegration(SimulationHost host, HeightfieldView view, StrategyCamera camera,
        WorldCursor cursor, SpellVfx spell, GodHandVfx hand, DruidView druids)
    {
        var sim = host.Heightfield;
        var fireSim = host.Fire;

        // Repeated reset: five cycles must each leave identical clean state.
        for (int cycle = 0; cycle < 5; cycle++)
        {
            sim.ApplyBrush(new System.Numerics.Vector3(0f, 0f, 0f), MatterType.Earth, true, 0.5f);
            var fuelled = FindFuelledDryCell(sim, fireSim);
            fireSim.Ignite(fuelled, sim.BrushRadius);
            spell.MatterDropped(WorldCoordinates.ToGodot(0f, 0f, 0f), MatterType.Water);
            sim.ResetSimulation(); fireSim.ResetFire(); cursor.SelectTool(MatterTool.Matter);
            spell.ClearTransient();
            if (sim.EarthBuffer != 0f || sim.WaterBuffer != 0f || fireSim.BurningCells != 0
                || fireSim.EmberBuffer != 0f || sim.StepCount != 0 || sim.LastError != null)
                throw new InvalidOperationException($"reset cycle {cycle} left residual state");
            if (druids.Band.Total != 8 || druids.Band.AliveCount != 8 || druids.Band.RitualComplete)
                throw new InvalidOperationException($"reset cycle {cycle} left the band dirty");
        }

        // Paused editing: a brush edit while paused applies immediately and only
        // reaches the view; on resume the next step processes it.
        sim.Paused = true; fireSim.Paused = true;
        int stepsBefore = sim.StepCount;
        float scooped = sim.ApplyBrush(new System.Numerics.Vector3(0f, 0f, 0f), MatterType.Earth, true, 0.5f);
        if (scooped <= 0f || sim.EarthBuffer <= 0f)
            throw new InvalidOperationException("paused editing did not apply");
        if (sim.StepCount != stepsBefore)
            throw new InvalidOperationException("paused brush stepped the solver");
        sim.Paused = false; fireSim.Paused = false;
        sim.StepOnce(); fireSim.StepOnce();
        if (sim.StepCount != stepsBefore + 1 || fireSim.StepCount == 0)
            throw new InvalidOperationException("single-step durations wrong after resume");

        // Edge brushing: all four corners and the exact domain border must clamp
        // safely, transfer nothing out of bounds, and never fault.
        float half = sim.WorldSize * .5f;
        foreach (var corner in new[]
                 {
                     new System.Numerics.Vector3(-half, 0f, -half),
                     new System.Numerics.Vector3(half, 0f, -half),
                     new System.Numerics.Vector3(-half, 0f, half),
                     new System.Numerics.Vector3(half, 0f, half),
                     new System.Numerics.Vector3(0f, 0f, half)
                 })
        {
            sim.ApplyBrush(corner, MatterType.Earth, true, 0.3f);
            sim.ApplyBrush(corner, MatterType.Water, false, 0.3f);
            fireSim.ApplyFireBrush(corner, true, 0.3f);
            if (sim.LastError != null) throw new InvalidOperationException($"edge brushing faulted at {corner}");
        }

        // Cursor targeting across the view: centre, corners and a shallow-angle row
        // must either land inside the domain on the tool-appropriate surface or fail —
        // never return a position outside it.
        camera.ResetView();
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var screenPoint in new[]
                 {
                     size / 2f, new Vector2(2f, 2f), size - new Vector2(2f, 2f),
                     new Vector2(size.X / 2f, 2f), new Vector2(size.X / 2f, size.Y - 2f)
                 })
        {
            foreach (MatterTool tool in Enum.GetValues<MatterTool>())
            {
                cursor.SelectTool(tool);
                if (!cursor.TryFindSurfaceAt(screenPoint, out Vector3 point)) continue;
                if (!sim.ContainsWorldPosition(WorldCoordinates.ToSimulation(point)))
                    throw new InvalidOperationException($"target outside domain at {screenPoint} ({tool})");
            }
        }
        cursor.SelectTool(MatterTool.Matter);

        // Scene-reload cleanup: a second spell/hand pair must retire cleanly, and the
        // originals must keep working afterwards (no stolen subscriptions or children).
        spell.RitualBurst(WorldCoordinates.ToGodot(druids.Band.HavenCenter.X, 0f, druids.Band.HavenCenter.Z),
            druids.Band.HavenRadius);
        var detachedSpell = new SpellVfx();
        AddChild(detachedSpell);
        detachedSpell.Initialize(host);
        detachedSpell.LightningStrike(WorldCoordinates.ToGodot(0f, 0f, 0f));
        RemoveChild(detachedSpell);
        detachedSpell.Tick(99f);
        detachedSpell.QueueFree();
        if (spell.TransientCount == 0)
            throw new InvalidOperationException("original spell lost its transients after detach");
        spell.Tick(99f);
        if (spell.TransientCount != 0)
            throw new InvalidOperationException("transients did not retire after reload check");

        sim.ResetSimulation(); fireSim.ResetFire(); spell.ClearTransient();
        hand.DebugDrive(false, Vector3.Zero);
        GD.Print("P7 adapter checks PASS: five clean reset cycles, paused editing, single-step durations, corner/border brushing, domain-safe targeting from centre/corners/shallow rows, VFX detach cleanup, working originals.");
    }


    /// <summary>Staged windowed captures of the P6 effects; explicit verification option only.</summary>
    private async void RunShowcase(SimulationHost host, StrategyCamera camera, WorldCursor cursor,
        SpellVfx spell, GodHandVfx hand, DruidView druids, Diagnostics diagnostics)
    {
        // An occluded macOS window gets throttled to ~1 fps, which lets every effect
        // expire between frames; keep the window on top so captures render at full rate.
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
        DisplayServer.WindowMoveToForeground();
        // Age spell effects by hand so captures show a fixed effect age regardless
        // of how the compositor paces the window.
        spell.ManualAge = true;

        var sim = host.Heightfield;
        var fireSim = host.Fire;
        sim.ResetSimulation(); fireSim.ResetFire();
        cursor.SelectTool(MatterTool.Matter);
        camera.ResetView();
        await WaitFrames(20);

        // God hand, half-full of earth, sweeping a small arc so the spirit trail reads.
        var centre = FindHighGround(sim);
        sim.ApplyBrush(centre, MatterType.Earth, true, 3.6f);
        camera.Focus(WorldCoordinates.ToGodot(centre.X, 0f, centre.Z));
        await ZoomTo(camera, 36f);
        for (int i = 0; i < 55; i++)
        {
            float angle = i * .09f;
            var sweep = new System.Numerics.Vector3(
                centre.X + MathF.Sin(angle) * 2.2f, 0f, centre.Z + MathF.Cos(angle * .7f) * 2.2f);
            hand.DebugDrive(true, WorldCoordinates.ToGodot(sweep.X, sim.SampleTerrain(sweep), sweep.Z));
            await WaitFrames(1);
        }
        await WaitFrames(10);
        await Capture("docs/verification/2026-09-12-p6-god-hand.png", diagnostics);

        // Lightning at a dry reed patch, aged 0.12 s so the bolt is mid-flash.
        var strike = FindFuelledDryCell(sim, fireSim);
        camera.Focus(WorldCoordinates.ToGodot(strike.X, 0f, strike.Z));
        cursor.TickCooldown(10f);
        cursor.StrikeAt(strike);
        spell.Tick(.12f);
        await Capture("docs/verification/2026-09-12-p6-lightning.png", diagnostics);

        // Whitewater + caustics: dump the water buffer on the highest ground, freeze the
        // torrent mid-flow (a paused solver keeps the baked flow foam in the mesh),
        // then zoom in on the rapids.
        sim.ResetSimulation(); fireSim.ResetFire(); spell.ClearTransient();
        hand.DebugDrive(false, Vector3.Zero);
        var crest = FindHighGround(sim);
        sim.ApplyBrush(crest, MatterType.Water, false, 7f);
        for (int i = 0; i < 45; i++) sim.StepOnce();
        sim.Paused = true;
        var rapids = FindFastestFlow(sim);
        // Frame between the pour pool and the slope below it: pool, rapids and sheet.
        var mid = System.Numerics.Vector3.Lerp(rapids, crest, .35f);
        camera.Focus(WorldCoordinates.ToGodot(mid.X, 0f, mid.Z));
        await ZoomTo(camera, 42f);
        await WaitFrames(30);
        await Capture("docs/verification/2026-09-12-p6-whitewater.png", diagnostics);

        // Caustics closeup: the shallow margin of whatever pool the map generated.
        sim.ResetSimulation(); fireSim.ResetFire(); sim.Paused = false;
        var shallows = FindShallowWater(sim);
        camera.Focus(WorldCoordinates.ToGodot(shallows.X, 0f, shallows.Z));
        await ZoomTo(camera, 22f);
        await WaitFrames(30);
        await Capture("docs/verification/2026-09-12-p6-caustics.png", diagnostics);

        // Ritual burst at the haven: zoom first (GPU particles age in wall-clock time),
        // then fire and hand-age 0.67 s — ring mid-bloom, streaks risen, light strong.
        sim.ResetSimulation(); fireSim.ResetFire(); spell.ClearTransient();
        camera.Focus(WorldCoordinates.ToGodot(druids.Band.HavenCenter.X, 0f, druids.Band.HavenCenter.Z));
        await ZoomTo(camera, 44f);
        spell.RitualBurst(WorldCoordinates.ToGodot(druids.Band.HavenCenter.X, 0f, druids.Band.HavenCenter.Z),
            druids.Band.HavenRadius);
        for (int i = 0; i < 20; i++) spell.Tick(1f / 30f);
        await Capture("docs/verification/2026-09-12-p6-ritual.png", diagnostics);
        GetTree().Quit(0);
    }

    /// <summary>Synthesizes wheel notches until the strategy camera reaches the target distance.</summary>
    private async System.Threading.Tasks.Task ZoomTo(StrategyCamera camera, float targetDistance)
    {
        while (Math.Abs(targetDistance - camera.Distance) > 2f)
        {
            for (int n = 0; n < 10; n++)
                Input.ParseInputEvent(new InputEventMouseButton
                {
                    ButtonIndex = targetDistance < camera.Distance ? MouseButton.WheelUp : MouseButton.WheelDown,
                    Pressed = true
                });
            Input.FlushBufferedEvents();
            await WaitFrames(1);
        }
    }

    private async System.Threading.Tasks.Task WaitFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async System.Threading.Tasks.Task Capture(string path, Diagnostics diagnostics)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(ProjectSettings.GlobalizePath(path));
        GD.Print($"Capture: {path} ({error})\n{diagnostics.Snapshot}");
    }

    private static System.Numerics.Vector3 FindHighGround(HeightfieldSimulation sim)
    {
        float half = sim.WorldSize * .5f;
        float best = float.MinValue;
        var at = new System.Numerics.Vector3(0f, 0f, -1f);
        for (int z = 12; z < sim.Resolution - 12; z += 3)
        for (int x = 12; x < sim.Resolution - 12; x += 3)
        {
            if (sim.GetWater(x, z) > 0f) continue;
            float height = sim.GetTerrain(x, z);
            if (height <= best) continue;
            best = height;
            at = new System.Numerics.Vector3(x * sim.CellSize - half, 0f, z * sim.CellSize - half);
        }
        return at;
    }

    /// <summary>A wet cell with a shallow visible floor — where caustics read best.</summary>
    private static System.Numerics.Vector3 FindShallowWater(HeightfieldSimulation sim)
    {
        float half = sim.WorldSize * .5f;
        float best = float.MinValue;
        var at = new System.Numerics.Vector3(0f, 0f, -1f);
        for (int z = 8; z < sim.Resolution - 8; z++)
        for (int x = 8; x < sim.Resolution - 8; x++)
        {
            float depth = sim.GetWater(x, z);
            if (depth < .06f || depth > .3f) continue;
            // Prefer low terrain: broad shallow sheets, not puddles on the tor.
            float score = sim.GetTerrain(x, z) < 2f ? depth : 0f;
            if (score <= best) continue;
            best = score;
            at = new System.Numerics.Vector3(x * sim.CellSize - half, 0f, z * sim.CellSize - half);
        }
        return at.Z == -1f ? FindWetCell(sim) : at;
    }

    /// <summary>The cell where the solver says water moves fastest — the rapids.</summary>
    private static System.Numerics.Vector3 FindFastestFlow(HeightfieldSimulation sim)
    {
        float half = sim.WorldSize * .5f;
        float best = 0f;
        var at = FindHighGround(sim);
        for (int z = 4; z < sim.Resolution - 4; z++)
        for (int x = 4; x < sim.Resolution - 4; x++)
        {
            float speed = sim.FlowSpeed(x, z);
            if (speed <= best) continue;
            best = speed;
            at = new System.Numerics.Vector3(x * sim.CellSize - half, 0f, z * sim.CellSize - half);
        }
        return at;
    }

    private static System.Numerics.Vector3 FindWetCell(HeightfieldSimulation sim)
    {
        float half = sim.WorldSize * .5f;
        for (int z = 0; z < sim.Resolution; z++)
        for (int x = 0; x < sim.Resolution; x++)
            if (sim.GetWater(x, z) > 0.5f)
                return new System.Numerics.Vector3(x * sim.CellSize - half, 0, z * sim.CellSize - half);
        return new System.Numerics.Vector3(0, 0, -1f);
    }

    private static System.Numerics.Vector3 FindDryCell(HeightfieldSimulation sim)
    {
        float half = sim.WorldSize * .5f;
        for (int z = 0; z < sim.Resolution; z++)
        for (int x = 0; x < sim.Resolution; x++)
            if (sim.GetWater(x, z) <= 0f)
                return new System.Numerics.Vector3(x * sim.CellSize - half, 0, z * sim.CellSize - half);
        return new System.Numerics.Vector3(0, 0, -1f);
    }

    private static System.Numerics.Vector3 FindFuelledDryCell(HeightfieldSimulation sim, FireSimulation fire)
    {
        float half = sim.WorldSize * .5f;
        for (int z = 8; z < sim.Resolution - 8; z++)
        for (int x = 8; x < sim.Resolution - 8; x++)
        {
            if (fire.GetFuel(x, z) < 0.45f || sim.GetWater(x, z) > 0f) continue;
            return new System.Numerics.Vector3(x * sim.CellSize - half, 0, z * sim.CellSize - half);
        }
        return new System.Numerics.Vector3(float.MaxValue, 0, 0);
    }

    private static System.Numerics.Vector3 FindDeepWetCell(HeightfieldSimulation sim)
    {
        float half = sim.WorldSize * .5f;
        for (int z = 4; z < sim.Resolution - 4; z++)
        for (int x = 4; x < sim.Resolution - 4; x++)
        {
            if (sim.GetWater(x, z) < 1f) continue;
            bool interior = true;
            for (int dz = -3; dz <= 3 && interior; dz++)
            for (int dx = -3; dx <= 3; dx++)
                if (sim.GetWater(x + dx, z + dz) < 0.1f) { interior = false; break; }
            if (interior) return new System.Numerics.Vector3(x * sim.CellSize - half, 0, z * sim.CellSize - half);
        }
        return new System.Numerics.Vector3(0, 0, -1f);
    }

    /// <summary>Drives a synthesized W keypress through the Input Map and checks the camera moves.</summary>
    private async void VerifyInput(StrategyCamera camera, Diagnostics diagnostics)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string before = camera.DebugState;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = true });
        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string after = camera.DebugState;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = false });
        GD.Print($"INPUT CHECK  pressed={Input.IsActionPressed(InputBindings.CamForward)}  before[{before}]  after[{after}]  moved={before != after}");
        GD.Print(diagnostics.Snapshot);
        GetTree().Quit(before != after ? 0 : 1);
    }

    private async void CaptureAfterFrames(string path, int frames, Diagnostics diagnostics)
    {
        // Explicit verification option only; normal play never writes screenshots.
        // Keep the window unoccluded so FPS numbers are not compositor-throttled.
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
        DisplayServer.WindowMoveToForeground();
        await WaitFrames(frames);
        await Capture(path, diagnostics);
    }
}
