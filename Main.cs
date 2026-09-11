using Godot;
using System;
using TheLevels.Simulation;
using TheLevels.UI;
using TheLevels.View;

namespace TheLevels;

public partial class Main : Node3D
{
    public override void _Ready()
    {
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
        var camera = new Camera3D { Name = "WorldCamera", Current = true, Fov = 60, Near = .2f, Far = 400 };
        AddChild(camera);
        // Fixed overview for P3. P4 will restore the source's interactive camera and start distance.
        camera.Position = WorldCoordinates.ToGodot(54, 102, -90);
        camera.LookAt(WorldCoordinates.ToGodot(0, 2.5f, 0));
        camera.HOffset = -12;
        var diagnostics = new Diagnostics { Name = "Diagnostics" };
        AddChild(diagnostics);
        diagnostics.Initialize(host, view);
        GD.Print($"The Levels P3: {host.Heightfield.Resolution}² world ready; engine {Engine.GetVersionInfo()["string"]}");

        string[] args = OS.GetCmdlineUserArgs();
        if (Array.Exists(args, a => a == "--verify-p3"))
        {
            try { VerifyView(host, view, diagnostics); }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); return; }
        }
        if (Array.Exists(args, a => a == "--demo-fire")) diagnostics.IgniteReeds();
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

    private async void CaptureAfterFrames(string path, Diagnostics diagnostics)
    {
        // Explicit verification option only; normal play never writes screenshots.
        for (int i = 0; i < 180; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(ProjectSettings.GlobalizePath(path));
        GD.Print($"P3 capture: {path} ({error})\n{diagnostics.Snapshot}");
    }
}
