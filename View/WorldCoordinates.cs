using Godot;

namespace TheLevels.View;

public static class WorldCoordinates
{
    // Preserve the Unity simulation's row direction; reflect Z only at the engine boundary.
    public static Vector3 ToGodot(float x, float y, float z) => new(x, y, -z);
    public static System.Numerics.Vector3 ToSimulation(Vector3 p) => new(p.X, p.Y, -p.Z);
}
