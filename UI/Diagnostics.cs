using Godot;
using TheLevels.Simulation;
using TheLevels.View;

namespace TheLevels.UI;

public partial class Diagnostics : CanvasLayer
{
    private SimulationHost host;
    private HeightfieldView view;
    private Label body;
    private Label state;
    private double timer;
    public string Snapshot { get; private set; } = "";

    public void Initialize(SimulationHost simulationHost, HeightfieldView heightfieldView)
    {
        host = simulationHost; view = heightfieldView;
        ProcessPriority = 200;
        var panel = new PanelContainer { Position = new Vector2(24, 24), CustomMinimumSize = new Vector2(276, 0) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(.045f, .065f, .06f, .94f),
            BorderColor = new Color(.63f, .55f, .32f, .5f),
            BorderWidthTop = 2,
            ContentMarginLeft = 20, ContentMarginRight = 20,
            ContentMarginTop = 18, ContentMarginBottom = 18,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        };
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);
        var title = new Label { Text = "THE LEVELS" };
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", new Color(.96f, .81f, .49f));
        column.AddChild(title);
        column.AddChild(new Label { Text = "SOMERSET · LIVING WETLANDS" });
        column.AddChild(new HSeparator());
        state = new Label(); column.AddChild(state);
        body = new Label();
        body.AddThemeFontSizeOverride("font_size", 14);
        body.AddThemeColorOverride("font_color", new Color(.82f, .87f, .82f));
        column.AddChild(body);
        column.AddChild(new HSeparator());
        var restart = new Button { Text = "Restart water flow" };
        restart.Pressed += () => { host.Heightfield.ResetSimulation(); host.Fire.ResetFire(); };
        column.AddChild(restart);
        var fireDemo = new Button { Text = "Ignite a reed bed" };
        fireDemo.Pressed += IgniteReeds;
        column.AddChild(fireDemo);
        column.AddChild(new Label { Text = "World preview · P3\nCamera & terrain tools come next." });
        Refresh();
    }

    public void IgniteReeds()
    {
        // Preview scenario, independent of the P4 cursor/lightning tool.
        var sim = host.Heightfield;
        float half = sim.WorldSize * .5f;
        for (int z = sim.Resolution / 2; z < sim.Resolution - 8; z++)
        for (int x = sim.Resolution / 2; x < sim.Resolution - 8; x++)
        {
            if (sim.GetWater(x, z) > .001f || host.Fire.GetFuel(x, z) < .45f || host.Fire.GetFire(x, z) > 0) continue;
            var point = new System.Numerics.Vector3(x * sim.CellSize - half, 0, z * sim.CellSize - half);
            int ignited = host.Fire.Ignite(point, 4f, .9f);
            if (ignited > 0) { GD.Print($"P3 preview: ignited {ignited} reed cells at {point}"); return; }
        }
    }

    public override void _Process(double delta)
    {
        timer -= delta;
        if (timer > 0) return;
        timer = .25;
        Refresh();
    }

    private void Refresh()
    {
        var sim = host.Heightfield;
        var fire = host.Fire;
        var metrics = sim.CalculateMetrics();
        state.Text = sim.LastError != null ? "FAULT · " + sim.LastError : sim.Paused ? "PAUSED" : "●  SIMULATION RUNNING";
        state.AddThemeColorOverride("font_color", sim.LastError != null ? Colors.OrangeRed : new Color(.63f, .85f, .60f));
        Snapshot = $"Grid  {sim.Resolution} × {sim.Resolution} · {sim.WorldSize:0} m\n" +
            $"Water step  {sim.StepCount} · Fire step  {fire.StepCount}\n\n" +
            $"Earth in world   {metrics.TerrainVolume:N1} m³\nWater in world   {metrics.WaterVolume:N1} m³\n" +
            $"Wet cells   {metrics.WetCells:N0}\nDeepest water   {metrics.MaximumWaterDepth:0.000} m\nPeak speed   {sim.MaxWaterSpeed:0.000} m/s\n\n" +
            $"Earth held   {sim.EarthBuffer:0.0} / {sim.EarthCapacity:0} m³\nWater held   {sim.WaterBuffer:0.0} / {sim.WaterCapacity:0} m³\nEmbers held   {fire.EmberBuffer:0.0} / {fire.EmberCapacity:0}\n" +
            $"Burning   {fire.BurningCells} · Charred   {fire.CharredCells}\n\n" +
            $"Water step   {sim.LastStepMilliseconds:0.00} ms\nFire tick   {host.LastFireAdvanceMilliseconds:0.00} ms\nMesh update   {view.LastUpdateMilliseconds:0.00} ms\nFPS   {Engine.GetFramesPerSecond()}\n\n" +
            $"Corrections   {sim.NegativeCorrections}\nInvalid values   {sim.InvalidValueCount}\nHeight clamps   {sim.HeightClampCount}";
        body.Text = Snapshot;
    }
}
