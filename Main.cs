using Godot;

namespace TheLevels;

public partial class Main : Node3D
{
    public override void _Ready()
    {
        GD.Print($"The Levels scaffold: engine {Engine.GetVersionInfo()["string"]} root={Name}");
    }
}
