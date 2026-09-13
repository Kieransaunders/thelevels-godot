using Godot;
using System;
using TheLevels.Core.Agents;
using TheLevels.Player;
using TheLevels.Simulation;
using TheLevels.UI;
using TheLevels.View;

namespace TheLevels.Agents;

/// <summary>
/// Single attach point for the villager settlement, so <c>Main.cs</c> wires it with one
/// line: node creation, reset hook, diagnostics line, the <c>--verify-villagers</c>
/// adapter gate and the <c>--village-capture</c> windowed capture all live here.
/// </summary>
public static class Village
{
    public static void Attach(Main main, SimulationHost host, WorldCursor cursor)
    {
        var villagers = new VillagerView { Name = "Villagers" };
        main.AddChild(villagers);
        villagers.Initialize(host);
        cursor.WorldReset += villagers.ResetAll;
        main.GetNode<Diagnostics>("Diagnostics").SetVillagers(villagers);

        string[] args = OS.GetCmdlineUserArgs();
        if (Array.Exists(args, a => a == "--verify-villagers"))
        {
            try { VerifyVillagers(main, host, villagers); }
            catch (Exception error) { GD.PushError(error.ToString()); main.GetTree().Quit(1); }
        }
        foreach (string arg in args)
            if (arg.StartsWith("--village-capture="))
                CaptureVillage(main, villagers, arg.Substring("--village-capture=".Length));
    }

    /// <summary>
    /// Villager adapter gate: composition and bodies (every mother carries a cradleboard,
    /// nobody else does), strolling while the world is paused, fire panic that drives the
    /// band out of the ring and re-homes them off the burning settlement, and reset.
    /// </summary>
    private static void VerifyVillagers(Main main, SimulationHost host, VillagerView villagers)
    {
        var village = villagers.Village;
        if (village.Total != 12 || village.AliveCount != 12)
            throw new InvalidOperationException("Settlement is not twelve living villagers");
        if (village.CountOf(VillagerKind.Man) != 5 || village.CountOf(VillagerKind.Woman) != 4
            || village.CountOf(VillagerKind.Mother) != 3)
            throw new InvalidOperationException("Settlement is not five men, four women, three mothers");
        var bodies = villagers.GetNode<Node3D>("Bodies");
        if (bodies.GetChildCount() != 12) throw new InvalidOperationException("Missing villager bodies");
        if (villagers.GetNode<Node3D>("Settlement").GetChildCount() != 5)
            throw new InvalidOperationException("Settlement is not four roundhouses and a hearth");
        for (int i = 0; i < village.Total; i++)
        {
            bool board = villagers.GetNodeOrNull<Node3D>($"Bodies/Villager {i}/Cradleboard") != null;
            if (board != (village.Agents[i].Kind == VillagerKind.Mother))
                throw new InvalidOperationException($"Cradleboard mismatch on villager {i}");
        }

        // Fire beside the settlement, tested against the fresh spawn ring: kindled 5.5 m
        // east, the whole ring (1.8–8.7 m) is deterministically inside the 9 m panic
        // radius but outside the 2 m ignite disc. Panic takes the band out of the ring,
        // and once the homes are off the burning village they re-home at the refuge.
        var threat = new System.Numerics.Vector3(village.HomeCenter.X + 5.5f, 0f, village.HomeCenter.Z);
        if (host.Fire.Ignite(threat, 2f, .9f) <= 0)
            throw new InvalidOperationException("No fuel beside the settlement to kindle");
        village.Advance(1f / 60f);
        bool anyPanic = false;
        foreach (VillagerAgent agent in village.Agents) anyPanic |= agent.State == VillagerState.Panic;
        if (!anyPanic) throw new InvalidOperationException("Fire beside the settlement caused no panic");
        // Panic clears beyond the ring + 2 m; dwell pauses mean nobody strolls back that fast.
        for (int frame = 0; frame < 270; frame++) village.Advance(1f / 60f);
        foreach (VillagerAgent agent in village.Agents)
        {
            if (agent.IsDead) throw new InvalidOperationException("A villager died fleeing a grass fire");
            if (agent.State == VillagerState.Panic)
                throw new InvalidOperationException("Villagers never left the panic ring");
        }
        var threatXz = new System.Numerics.Vector2(threat.X, threat.Z);
        foreach (VillagerAgent agent in village.Agents)
            if (System.Numerics.Vector2.Distance(
                    new System.Numerics.Vector2(agent.Home.X, agent.Home.Z), threatXz) <= VillagerManager.PanicRadius)
                throw new InvalidOperationException("A villager re-homed inside the burning settlement");

        // Reset drops every old body, respawns the full settlement back home.
        host.Fire.ResetFire();
        villagers.ResetAll();
        if (village.Total != 12 || village.AliveCount != 12)
            throw new InvalidOperationException("Reset did not restore the settlement");
        int live = 0;
        foreach (Node child in bodies.GetChildren())
            if (!child.IsQueuedForDeletion()) live++;
        if (live != 12) throw new InvalidOperationException($"Reset left {live} live bodies");
        if (Horizontal(village.Agents[0].Home, village.HomeCenter) > 0.01f)
            throw new InvalidOperationException("Reset did not restore homes");

        // Villagers keep strolling while water and fire are paused; the first dwell pause
        // is at most 4.5 s, so within six seconds somebody must have walked somewhere.
        host.Heightfield.Paused = true; host.Fire.Paused = true;
        var start = new System.Numerics.Vector3[village.Total];
        for (int i = 0; i < village.Total; i++) start[i] = village.Agents[i].Position;
        for (int frame = 0; frame < 360; frame++) village.Advance(1f / 60f);
        float furthest = 0f;
        for (int i = 0; i < village.Total; i++)
            furthest = MathF.Max(furthest, Horizontal(village.Agents[i].Position, start[i]));
        if (furthest < 0.5f) throw new InvalidOperationException("Villagers froze with the simulation");
        host.Heightfield.Paused = false; host.Fire.Paused = false;

        // Leave the world clean for the gates that follow: this runs before the P3
        // adapter gate, whose first publish must be a clean one.
        host.Heightfield.ResetSimulation();
        host.Fire.ResetFire();
        villagers.ResetAll();
        main.GetNode<HeightfieldView>("HeightfieldView").PublishChanges();

        GD.Print("VILLAGER CHECKS PASS: twelve villagers (5 men, 4 women, 3 mothers), cradleboards on mothers only, four roundhouses + hearth, strolling while paused, panic flight + refuge re-homing, reset respawn.");
    }

    /// <summary>Windowed capture of the settlement; explicit verification option only.</summary>
    private static async void CaptureVillage(Main main, VillagerView villagers, string path)
    {
        // An occluded macOS window throttles to ~1 fps; keep it on top for full-rate frames.
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
        DisplayServer.WindowMoveToForeground();
        var camera = main.GetNode<StrategyCamera>("WorldCamera");
        var home = villagers.Village.HomeCenter;
        camera.Focus(WorldCoordinates.ToGodot(home.X, 0f, home.Z));
        while (MathF.Abs(34f - camera.Distance) > 2f)
        {
            for (int n = 0; n < 10; n++)
                Input.ParseInputEvent(new InputEventMouseButton
                    { ButtonIndex = MouseButton.WheelUp, Pressed = true });
            Input.FlushBufferedEvents();
            await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // Let the band stroll out of their spawn ring before the shutter.
        for (int i = 0; i < 150; i++)
            await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
        await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = main.GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(ProjectSettings.GlobalizePath(path));
        GD.Print($"Capture: {path} ({error})");
        main.GetTree().Quit(0);
    }

    private static float Horizontal(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
        => System.Numerics.Vector2.Distance(
            new System.Numerics.Vector2(a.X, a.Z), new System.Numerics.Vector2(b.X, b.Z));
}
