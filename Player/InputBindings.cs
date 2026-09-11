using Godot;

namespace TheLevels.Player;

/// <summary>
/// Registers every game action in the Input Map at startup so all gameplay input
/// goes through named actions (port plan P4). Key assignments mirror the Unity
/// prototype's direct key reads.
/// </summary>
public static class InputBindings
{
    public const string ToolEarth = "tool_earth";
    public const string ToolWater = "tool_water";
    public const string ToolFire = "tool_fire";
    public const string ToolLightning = "tool_lightning";
    public const string ResetWorld = "reset_world";
    public const string TogglePause = "toggle_pause";
    public const string StepOnce = "step_once";
    public const string FocusCamera = "focus_camera";
    public const string ToggleMetrics = "toggle_metrics";
    public const string CamForward = "cam_forward";
    public const string CamBack = "cam_back";
    public const string CamLeft = "cam_left";
    public const string CamRight = "cam_right";
    public const string CamRotateCcw = "cam_rotate_ccw";
    public const string CamRotateCw = "cam_rotate_cw";
    public const string CamTiltUp = "cam_tilt_up";
    public const string CamTiltDown = "cam_tilt_down";
    public const string Scoop = "scoop";
    public const string Drop = "drop";

    public static void Register()
    {
        BindKey(ToolEarth, Key.Key1);
        BindKey(ToolWater, Key.Key2);
        BindKey(ToolFire, Key.Key3);
        BindKey(ToolLightning, Key.Key4);
        BindKey(ResetWorld, Key.R);
        BindKey(TogglePause, Key.Space);
        BindKey(StepOnce, Key.N);
        BindKey(FocusCamera, Key.F);
        BindKey(ToggleMetrics, Key.F1);
        BindKey(CamForward, Key.W);
        BindKey(CamBack, Key.S);
        BindKey(CamLeft, Key.A);
        BindKey(CamRight, Key.D);
        BindKey(CamRotateCcw, Key.Q);
        BindKey(CamRotateCw, Key.E);
        BindKey(CamTiltUp, Key.Up);
        BindKey(CamTiltDown, Key.Down);
        BindMouse(Scoop, MouseButton.Left);
        BindMouse(Drop, MouseButton.Right);
    }

    private static void BindKey(string action, Key key)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { Keycode = key });
    }

    private static void BindMouse(string action, MouseButton button)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }
}
