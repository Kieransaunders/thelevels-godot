using Godot;
using TheLevels.Player;
using TheLevels.Simulation;
using TheLevels.View;

namespace TheLevels.UI;

public partial class Diagnostics : CanvasLayer
{
    private SimulationHost host;
    private HeightfieldView view;
    private WorldCursor cursor;
    private Label body;
    private Label state;
    private Label controls;
    private PanelContainer panel;
    private double timer;
    private bool visible = true;
    public string Snapshot { get; private set; } = "";

    public void Initialize(SimulationHost simulationHost, HeightfieldView heightfieldView, WorldCursor worldCursor)
    {
        host = simulationHost; view = heightfieldView; cursor = worldCursor;
        ProcessPriority = 200;
        panel = new PanelContainer { Position = new Vector2(24, 24), CustomMinimumSize = new Vector2(276, 0) };
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

        // Unity OnGUI controls strip, kept verbatim.
        controls = new Label
        {
            Text = "1 EARTH   2 WATER   3 FIRE   4 LIGHTNING   LEFT SCOOP / HOLD-STRIKE   RIGHT DROP\n" +
                   "WASD MOVE   Q/E ROTATE   ↑/↓ TILT   WHEEL ZOOM   F FOCUS   R RESET   SPACE PAUSE   N STEP   F1 METRICS"
        };
        controls.AddThemeFontSizeOverride("font_size", 13);
        controls.AddThemeColorOverride("font_color", new Color(.70f, .74f, .68f));
        var controlsPanel = new PanelContainer
        {
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = 16, OffsetRight = -16, OffsetTop = -64, OffsetBottom = -16,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        controlsPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(.03f, .04f, .04f, .8f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 10, ContentMarginBottom = 10,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        });
        controlsPanel.AddChild(controls);
        AddChild(controlsPanel);
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed(InputBindings.ToggleMetrics))
            visible = !visible;
        panel.Visible = visible;

        timer -= delta;
        if (timer > 0) return;
        timer = .25;
        Refresh();
    }

    /// <summary>Preview helper kept for the headless adapter gates; not on the UI any more.</summary>
    public void IgniteReeds()
    {
        var sim = host.Heightfield;
        float half = sim.WorldSize * .5f;
        for (int z = sim.Resolution / 2; z < sim.Resolution - 8; z++)
        for (int x = sim.Resolution / 2; x < sim.Resolution - 8; x++)
        {
            if (sim.GetWater(x, z) > .001f || host.Fire.GetFuel(x, z) < .45f || host.Fire.GetFire(x, z) > 0) continue;
            var point = new System.Numerics.Vector3(x * sim.CellSize - half, 0, z * sim.CellSize - half);
            int ignited = host.Fire.Ignite(point, 4f, .9f);
            if (ignited > 0) { GD.Print($"Preview: ignited {ignited} reed cells at {point}"); return; }
        }
    }

    private void Refresh()
    {
        var sim = host.Heightfield;
        var fire = host.Fire;
        var metrics = sim.CalculateMetrics();
        state.Text = sim.LastError != null ? "FAULT · " + sim.LastError : sim.Paused ? "PAUSED" : "●  SIMULATION RUNNING";
        state.AddThemeColorOverride("font_color", sim.LastError != null ? Colors.OrangeRed : new Color(.63f, .85f, .60f));
        Snapshot = $"Tool  {cursor?.SelectedTool}\n" +
            $"Grid  {sim.Resolution} × {sim.Resolution} · {sim.WorldSize:0} m\n" +
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
