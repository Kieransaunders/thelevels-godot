using Godot;
using System;
using TheLevels.Agents;
using TheLevels.Core.Levels;
using TheLevels.Core.Simulation;
using TheLevels.Player;
using TheLevels.Simulation;
using TheLevels.UI;
using TheLevels.View;

namespace TheLevels;

public partial class Main
{
    /// <summary>Exercise the default scene and actual mouse/key input; core tests prove the full crossing.</summary>
    private async void VerifyFirstCrossing(SimulationHost host, StrategyCamera camera, WorldCursor cursor, DruidView druids)
    {
        try
        {
            await WaitFrames(3);
            var mission = host.Mission ?? throw new InvalidOperationException("Default scene did not load the first crossing");
            var hud = GetNode<CrossingHud>("CrossingHud");
            if (druids.Band != mission.Band || druids.Band.Total != 12
                || druids.GetNode<Node3D>("Druids").GetChildCount() != 12)
                throw new InvalidOperationException("The mission's twelve people were not given bodies");
            if (mission.RouteSafe || mission.Band.RitualComplete || hud.ObjectiveText != "Follow your people")
                throw new InvalidOperationException("The opening skipped its first objective");
            if (GetNodeOrNull<FirstCrossingView>("FirstCrossing") == null || host.Heightfield.WaterCapacity != 3)
                throw new InvalidOperationException("Missing scenery or water grip cap");

            KeyEvent(Key.Key4, true);
            await WaitFrames(2);
            KeyEvent(Key.Key4, false);
            if (cursor.SelectedTool != MatterTool.Matter) throw new InvalidOperationException("Lightning bypassed the tutorial tool limit");

            // Brush through the solver the way every other adapter gate does. Synthesized
            // mouse input cannot be used here: Godot reports the real OS cursor through
            // GetViewport().GetMousePosition(), so headless (a 64 px stub window) it never
            // moves, and windowed it lands wherever the physical mouse happens to sit. The
            // full crossing is covered by Tests/FirstCrossingTests.cs instead.
            var sim = host.Heightfield;
            float scooped = sim.ApplyBrush(FirstCrossing.EarthBank, MatterType.Earth, true, 1f);
            if (scooped <= 0f || sim.EarthBuffer <= 0f)
                throw new InvalidOperationException("Scoop did not gather earth at the marked bank");
            float held = sim.EarthBuffer;
            sim.ApplyBrush(System.Numerics.Vector3.Zero, MatterType.Earth, false, 1f);
            if (sim.EarthBuffer >= held) throw new InvalidOperationException("Pour did not use carried earth");

            KeyEvent(Key.R, true);
            await WaitFrames(2);
            KeyEvent(Key.R, false);
            await WaitFrames(2);
            hud.UpdateText();
            if (host.Heightfield.EarthBuffer != 0 || mission.Stage != CrossingStage.Approach
                || mission.Band.AliveCount != 12 || mission.RouteSafe || camera.Distance != 70f)
                throw new InvalidOperationException("R did not restore the opening mission, hand and camera");

            GD.Print("First crossing adapter checks PASS: default mission, twelve bodies, objective HUD, capped water, tool lock, scoop/pour accounting, R resets people/route/hand/camera.");
        }

        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
        finally
        {
            KeyEvent(Key.R, false);
            KeyEvent(Key.Key4, false);
        }
    }

    private static void KeyEvent(Key key, bool pressed) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
}
