using Godot;
using TheLevels.Core.Levels;
using TheLevels.Simulation;

namespace TheLevels.UI;

/// <summary>Player objectives and carried matter, independent of the F1 diagnostics.</summary>
public partial class CrossingHud : CanvasLayer
{
    private SimulationHost host;
    private Label objective, instruction, progress, carry;
    private ProgressBar route;
    private double refresh;
    public string ObjectiveText => objective?.Text ?? "";

    public void Initialize(SimulationHost simulationHost)
    {
        host = simulationHost;
        Layer = 2;
        var panel = new PanelContainer
        {
            Position = new Vector2(24, 24), CustomMinimumSize = new Vector2(360, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(.035f, .065f, .065f, .94f),
            BorderColor = new Color(.73f, .61f, .36f), BorderWidthTop = 3,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 18, ContentMarginBottom = 18,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        });
        AddChild(panel);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);
        AddLabel(column, "THE RAISED WAY", 23, new Color(.96f, .83f, .57f));
        AddLabel(column, "01   /   THE FIRST CROSSING", 12, new Color(.66f, .75f, .71f));
        column.AddChild(new HSeparator());
        objective = AddLabel(column, "", 20, Colors.White);
        instruction = AddLabel(column, "", 15, new Color(.82f, .87f, .83f));
        instruction.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        instruction.CustomMinimumSize = new Vector2(316, 0);
        route = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5), MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(route);
        progress = AddLabel(column, "", 13, new Color(.70f, .83f, .76f));
        column.AddChild(new HSeparator());
        carry = AddLabel(column, "", 14, new Color(.96f, .83f, .57f));
        UpdateText();
    }

    private static Label AddLabel(VBoxContainer column, string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        column.AddChild(label);
        return label;
    }

    public override void _Process(double delta)
    {
        if (host == null) return;
        refresh -= delta;
        if (refresh > 0) return;
        refresh = .15;
        UpdateText();
    }

    public void UpdateText()
    {
        var mission = host.Mission;
        (string title, string body) = mission.Stage switch
        {
            CrossingStage.Approach => ("Follow your people", "Your people are heading for the broken causeway. Follow the pale route to the water. WASD moves the view; scroll to zoom. F finds your people."),
            CrossingStage.Build when !mission.CarryingEarth => ("Gather earth", "The way has washed away. Hold LEFT over the marked earth bank to gather dry soil. The amber ring takes earth; blue takes water."),
            CrossingStage.Build => ("Raise the first gap", "Hold RIGHT and sweep earth along the red route. Make a broad, gently sloping path above the water. Refill at the earth bank as needed; allow the water to settle."),
            CrossingStage.Crossing => ("Let your people cross", "The way is holding. Keep the marked path dry and intact while all twelve cross to the far bank."),
            CrossingStage.Complete => ("The first way holds", "All twelve are safely across. You have raised the first section of the causeway. Explore the earth and water, or press R to try the crossing again."),
            _ => ("Someone was lost", "The crossing needs all twelve people. Press R to bring everyone back and rebuild the way.")
        };
        objective.Text = host.Heightfield.Paused ? "Paused" : title;
        instruction.Text = host.Heightfield.Paused ? "Press SPACE to continue. You can still shape the ground while the world is paused." : body;
        route.Value = mission.SafeFraction * 100;
        progress.Text = $"Route {mission.SafeFraction:P0} safe  ·  Across {mission.ArrivedCount}/12  ·  Alive {mission.Band.AliveCount}/12";
        carry.Text = $"Earth  {host.Heightfield.EarthBuffer:0} / 45 m³    Water  {host.Heightfield.WaterBuffer:0.0} / 3 m³";
    }
}
