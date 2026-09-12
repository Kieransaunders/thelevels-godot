using Godot;
using System;
using TheLevels.View;

namespace TheLevels.Player;

/// <summary>
/// Port of StrategyCameraController: WASD pan in view space, Q/E rotate, arrows tilt,
/// wheel zoom, F focus. Focus point lives in simulation coordinates; the Unity
/// spherical-to-position math is preserved and converted at the engine boundary.
/// </summary>
public partial class StrategyCamera : Camera3D
{
    private const float PanSpeed = 32f;
    private const float ZoomSpeed = 5f;
    private const float RotateSpeed = 75f;
    // Unity wheel notches carry ~5 units of scroll value; one Godot notch ≈ one Unity notch.
    private const float ZoomPerNotch = ZoomSpeed * 0.01f * 5f;

    private System.Numerics.Vector3 focus = new(0f, 2.5f, 0f);
    private float distance = 104f;
    private float yaw = -35f;
    private float pitch = 52f;

    public override void _Ready()
    {
        Fov = 42f;   // Unity bootstrap: camera.fieldOfView = 42
        Near = 0.1f;
        Far = 500f;
        Current = true;
        ResetView();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float moveX = (Input.IsActionPressed(InputBindings.CamRight) ? 1 : 0)
                    - (Input.IsActionPressed(InputBindings.CamLeft) ? 1 : 0);
        float moveY = (Input.IsActionPressed(InputBindings.CamForward) ? 1 : 0)
                    - (Input.IsActionPressed(InputBindings.CamBack) ? 1 : 0);

        // Unity: right and forward rotated by yaw around Y (sim space).
        var right = new System.Numerics.Vector3(MathF.Cos(yaw * MathF.PI / 180f), 0f, -MathF.Sin(yaw * MathF.PI / 180f));
        var forward = new System.Numerics.Vector3(MathF.Sin(yaw * MathF.PI / 180f), 0f, MathF.Cos(yaw * MathF.PI / 180f));
        focus += (right * moveX + forward * moveY) * (PanSpeed * dt * distance / 104f);

        if (Input.IsActionPressed(InputBindings.CamRotateCcw)) yaw -= RotateSpeed * dt;
        if (Input.IsActionPressed(InputBindings.CamRotateCw)) yaw += RotateSpeed * dt;
        if (Input.IsActionPressed(InputBindings.CamTiltUp)) pitch += RotateSpeed * dt;
        if (Input.IsActionPressed(InputBindings.CamTiltDown)) pitch -= RotateSpeed * dt;
        pitch = Math.Clamp(pitch, 12f, 85f);
        focus.X = Math.Clamp(focus.X, -44f, 44f);
        focus.Z = Math.Clamp(focus.Z, -44f, 44f);
        ApplyView();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
                distance = Math.Clamp(distance - ZoomPerNotch, 24f, 150f);
            else if (button.ButtonIndex == MouseButton.WheelDown)
                distance = Math.Clamp(distance + ZoomPerNotch, 24f, 150f);
        }
    }

    public string DebugState => $"focus {focus.X:0.0},{focus.Z:0.0}  dist {distance:0}  yaw {yaw:0}";

    public void Focus(Vector3 worldPoint)
    {
        var sim = WorldCoordinates.ToSimulation(worldPoint);
        focus = new System.Numerics.Vector3(sim.X, 2f, sim.Z);
        ApplyView();
    }

    public void ResetView()
    {
        focus = new System.Numerics.Vector3(0f, 2.5f, 0f);
        distance = 104f;
        yaw = -35f;
        pitch = 52f;
        ApplyView();
    }

    private void ApplyView()
    {
        // Unity: position = focus - Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * distance,
        // where Unity's forward under positive pitch points downward.
        float radPitch = pitch * MathF.PI / 180f;
        float radYaw = yaw * MathF.PI / 180f;
        var forward = new System.Numerics.Vector3(
            MathF.Cos(radPitch) * MathF.Sin(radYaw),
            -MathF.Sin(radPitch),
            MathF.Cos(radPitch) * MathF.Cos(radYaw));
        var position = focus - forward * distance;
        Position = WorldCoordinates.ToGodot(position.X, position.Y, position.Z);
        LookAt(WorldCoordinates.ToGodot(focus.X, focus.Y, focus.Z), Vector3.Up);
    }
}
