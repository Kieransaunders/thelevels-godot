using Godot;
using System;
using TheLevels.Player;
using TheLevels.Core.Simulation;
using TheLevels.Simulation;
using TheLevels.UI;
using TheLevels.View;

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
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(.12f, .17f, .18f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(.72f, .79f, .82f),
            AmbientLightEnergy = .65f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            FogEnabled = true,
            FogLightColor = new Color(.55f, .62f, .59f),
            FogDensity = .0007f,
            FogSkyAffect = 0f
        };
        AddChild(new WorldEnvironment { Name = "WetlandEnvironment", Environment = environment });
        AddChild(new DirectionalLight3D
        {
            Name = "MorningSun", RotationDegrees = new Vector3(-46, -32, 0),
            LightColor = new Color(1, .82f, .59f), LightEnergy = 1.15f, ShadowEnabled = true
        });

        var camera = new StrategyCamera { Name = "WorldCamera" };
        AddChild(camera);
        var cursor = new WorldCursor { Name = "WorldCursor" };
        AddChild(cursor);
        cursor.Initialize(host, camera);

        var diagnostics = new Diagnostics { Name = "Diagnostics" };
        AddChild(diagnostics);
        diagnostics.Initialize(host, view, cursor);
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
        foreach (string arg in args)
            if (arg.StartsWith("--capture=")) CaptureAfterFrames(arg.Substring("--capture=".Length), diagnostics);
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

        // Tool switching drives matter selection.
        cursor.SelectTool(MatterTool.Earth);
        if (cursor.SelectedMatter != MatterType.Earth) throw new InvalidOperationException("Earth tool matter");
        cursor.SelectTool(MatterTool.Water);
        if (cursor.SelectedMatter != MatterType.Water) throw new InvalidOperationException("Water tool matter");

        // Targeting: from the start view, screen centre must land inside the domain,
        // and a corner ray may leave it. Water-tool targeting snaps to the water surface.
        camera.ResetView();
        var size = GetViewport().GetVisibleRect().Size;
        if (!cursor.TryFindSurfaceAt(size / 2f, out Vector3 centre))
            throw new InvalidOperationException("Centre targeting failed from start view");
        if (!sim.ContainsWorldPosition(WorldCoordinates.ToSimulation(centre)))
            throw new InvalidOperationException("Target outside domain");
        var terrainUnder = sim.SampleTerrain(WorldCoordinates.ToSimulation(centre));
        if (MathF.Abs(centre.Y - terrainUnder) > 1.5f)
            throw new InvalidOperationException($"Terrain targeting off surface: y={centre.Y:0.00} terrain={terrainUnder:0.00}");

        // Find a wet screen point by projecting a known wet cell; water tool must target
        // the water surface above terrain there.
        System.Numerics.Vector3 wet = FindWetCell(sim);
        if (wet.Z == -1f) throw new InvalidOperationException("No wet cell");
        Vector2 wetScreen = camera.UnprojectPosition(WorldCoordinates.ToGodot(wet.X, 0, wet.Z));
        if (cursor.TryFindSurfaceAt(wetScreen, out Vector3 wetPoint))
        {
            float surface = sim.SampleSurface(WorldCoordinates.ToSimulation(wetPoint));
            if (MathF.Abs(wetPoint.Y - surface) > 1.0f)
                throw new InvalidOperationException("Water tool did not target the water surface");
        }

        // Earth brush: scoop fills the buffer with real volume accounting.
        cursor.SelectTool(MatterTool.Earth);
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

        GD.Print("P4 adapter checks PASS: tool/matter switching, screen targeting on terrain, water-surface targeting, earth brush accounting, lightning kindle + cooldown + water boiling, pause/single-step, reset.");
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

    private async void CaptureAfterFrames(string path, Diagnostics diagnostics)
    {
        // Explicit verification option only; normal play never writes screenshots.
        for (int i = 0; i < 180; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(ProjectSettings.GlobalizePath(path));
        GD.Print($"Capture: {path} ({error})\n{diagnostics.Snapshot}");
    }
}
