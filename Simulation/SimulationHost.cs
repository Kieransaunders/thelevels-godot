using Godot;
using System.Diagnostics;
using TheLevels.Core.Simulation;

namespace TheLevels.Simulation;

public partial class SimulationHost : Node
{
    public HeightfieldSimulation Heightfield { get; private set; }
    public FireSimulation Fire { get; private set; }
    public double LastFireAdvanceMilliseconds { get; private set; }
    private readonly Stopwatch watch = new();

    public override void _Ready()
    {
        Heightfield = new HeightfieldSimulation();
        Fire = new FireSimulation(Heightfield);
        Heightfield.Faulted += OnFault;
        ProcessPriority = -100;
    }

    public override void _Process(double delta)
    {
        // Each core owns its independent capped accumulator: water 1/30 s, fire 0.1 s.
        // Tools will process after this host and before the view publishes the frame.
        Heightfield.Advance((float)delta);
        watch.Restart();
        Fire.Advance((float)delta);
        LastFireAdvanceMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    private void OnFault(string message) => GD.PushError(message);

    public override void _ExitTree()
    {
        if (Heightfield != null) Heightfield.Faulted -= OnFault;
    }
}
