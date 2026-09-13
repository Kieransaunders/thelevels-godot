using Godot;
using System;
using TheLevels.Agents;
using TheLevels.Player;
using TheLevels.Core.Levels;
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

        string[] args = OS.GetCmdlineUserArgs();
        if (Array.Exists(args, a => a == "--list-levels"))
        {
            // "level <name> <title>" — verify.sh greps this to drive its per-level loop.
            foreach (Level entry in Catalogue.All) GD.Print($"level {entry.Name} {entry.Title}");
            GetTree().Quit();
            return;
        }
        // L cycles levels in-game; --level= picks one at launch. The L key wins, so a
        // developer can start anywhere and walk the catalogue without relaunching.
        string requested = RequestedLevel ?? ReadLevelArg(args);
        RequestedLevel = null;
        if (!Catalogue.Exists(requested))
            GD.PushWarning($"Unknown level '{requested}'; loading {Catalogue.Default.Name}. --list-levels shows them all.");
        var host = new SimulationHost { Name = "SimulationHost", LevelName = requested };
        currentLevel = Catalogue.Find(requested).Name;
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
        if (host.Mission != null) camera.SetHome(new System.Numerics.Vector3(3, 2.5f, 0), 70f);
        var cursor = new WorldCursor { Name = "WorldCursor" };
        AddChild(cursor);
        cursor.Initialize(host, camera);

        var druids = new DruidView { Name = "Druids" };
        AddChild(druids);
        druids.Initialize(host);
        cursor.WorldReset += druids.ResetAll;

        var forest = new FloraView { Name = "Forest" };
        AddChild(forest);
        forest.Initialize(host);
        cursor.WorldReset += forest.ResetAll;

        var wildlife = new WildlifeView { Name = "Wildlife" };
        AddChild(wildlife);
        wildlife.Initialize(host);
        cursor.WorldReset += wildlife.ResetAll;

        var diagnostics = new Diagnostics { Name = "Diagnostics" };
        AddChild(diagnostics);
        diagnostics.Initialize(host, view, cursor, druids);
        Village.Attach(this, host, cursor); // villagers: men, women, mothers with babies — wiring lives in Agents/Village.cs

        var spell = new SpellVfx { Name = "SpellVfx" };
        AddChild(spell);
        spell.Initialize(host);
        cursor.Spell = spell;
        cursor.WorldReset += spell.ClearTransient;
        var hand = new GodHandVfx { Name = "GodHand" };
        AddChild(hand);
        hand.Initialize(cursor, host);
        druids.Spell = spell;

        if (host.Mission != null)
        {
            var crossing = new FirstCrossingView { Name = "FirstCrossing" };
            AddChild(crossing);
            crossing.Initialize(host);
            var missionHud = new CrossingHud { Name = "CrossingHud" };
            AddChild(missionHud);
            missionHud.Initialize(host);
        }

        GD.Print($"The Levels: {host.Level.Title} — {host.Heightfield.Resolution}² world ready; "
                 + $"engine {Engine.GetVersionInfo()["string"]}");

        if (Array.Exists(args, a => a == "--verify-level")) VerifyLevelBoots(host, view, camera);
        if (Array.Exists(args, a => a == "--verify-level-hop")) VerifyLevelHop(host);
        if (Array.Exists(args, a => a == "--verify-level-one")) VerifyFirstCrossing(host, camera, cursor, druids);

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
        if (Array.Exists(args, a => a == "--verify-p8"))
        {
            try { VerifyWildlife(host, forest, wildlife); }
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

    /// <summary>Set by the L key before a scene reload, so the rebuild lands on the next level.</summary>
    private static string RequestedLevel;

    public override void _UnhandledInput(InputEvent @event)
    {
        // Test the event, not the polled action: _UnhandledInput runs off the event queue,
        // where IsActionJustPressed's frame-scoped state is not reliable.
        if (!@event.IsActionPressed(InputBindings.NextLevel)) return;
        LoadNextLevel();
    }

    private string currentLevel = Catalogue.Default.Name;

    /// <summary>Rebuilds the scene on the next catalogue entry. Bound to L; also used by the gate.</summary>
    internal void LoadNextLevel()
    {
        RequestedLevel = Catalogue.Next(currentLevel).Name;
        GD.Print($"Loading {RequestedLevel}…");
        GetTree().ReloadCurrentScene();
    }

    private static string ReadLevelArg(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--level=")) return arg["--level=".Length..];
            // --sandbox is the old spelling, kept so existing scripts keep working.
            if (arg == "--sandbox") return "sandbox";
        }
        return Catalogue.Default.Name;
    }
}
