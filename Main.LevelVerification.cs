using Godot;
using System;
using TheLevels.Player;
using TheLevels.Simulation;
using TheLevels.View;

namespace TheLevels;

public partial class Main
{
    /// <summary>
    /// The scene-boot check every level owes, run once per entry in the catalogue. The map
    /// itself is covered engine-free by Tests/CatalogueTests; what only a real scene can
    /// prove is that this level's wiring assembles — that nothing level-specific hands a
    /// null to a view, and that a reset survives it.
    /// </summary>
    private async void VerifyLevelBoots(SimulationHost host, HeightfieldView view, StrategyCamera camera)
    {
        try
        {
            await WaitFrames(5);
            string name = host.Level.Name;
            if (string.IsNullOrEmpty(name) || host.Heightfield == null || host.Fire == null)
                throw new InvalidOperationException($"'{LevelArgument()}' did not build a simulation");
            if (host.Level.Name != LevelArgument() && LevelArgument().Length > 0)
                throw new InvalidOperationException($"asked for '{LevelArgument()}', got '{name}'");
            if (host.Heightfield.Resolution < 33)
                throw new InvalidOperationException($"{name}: resolution {host.Heightfield.Resolution}");
            if (view.RebuildCount == 0) throw new InvalidOperationException($"{name}: the terrain never rendered");
            foreach (string required in new[] { "WorldCamera", "WorldCursor", "Druids", "Forest" })
                if (GetNodeOrNull(required) == null)
                    throw new InvalidOperationException($"{name}: scene is missing {required}");

            await WaitFrames(10);
            if (host.Heightfield.LastError != null)
                throw new InvalidOperationException($"{name} faulted: {host.Heightfield.LastError}");
            if (host.Heightfield.InvalidValueCount != 0 || host.Heightfield.NegativeCorrections != 0)
                throw new InvalidOperationException($"{name}: solver corrections on a fresh map");

            host.Heightfield.ResetSimulation();
            host.Fire.ResetFire();
            camera.ResetView();
            await WaitFrames(3);
            if (host.Heightfield.EarthBuffer != 0f || host.Heightfield.StepCount != 0)
                throw new InvalidOperationException($"{name}: reset left the hand or clock dirty");

            GD.Print($"Level boot checks PASS ({name}): simulation built, terrain rendered, "
                     + "scene assembled, thirty frames clean, reset clean.");
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// The L key rebuilds the scene on the next catalogue entry. The check cannot await the
    /// reload — the awaiting node is freed with the old scene, and its continuation never
    /// runs — so the outgoing scene records what it expects and the rebuilt one reports.
    /// </summary>
    private static string hopExpected;

    private async void VerifyLevelHop(SimulationHost host)
    {
        try
        {
            await WaitFrames(5);
            if (hopExpected == null)
            {
                hopExpected = TheLevels.Core.Levels.Catalogue.Next(host.Level.Name).Name;
                LoadNextLevel();
                return;
            }
            if (host.Level.Name != hopExpected)
                throw new InvalidOperationException($"L should have loaded '{hopExpected}', got '{host.Level.Name}'");
            GD.Print($"Level hop checks PASS: L rebuilt the scene onto {host.Level.Name}.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static string LevelArgument()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--level=")) return arg["--level=".Length..];
            if (arg == "--sandbox") return "sandbox";
        }
        return "";
    }
}
