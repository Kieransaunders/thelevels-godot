namespace TheLevels.Core.Simulation;

/// <summary>
/// Placeholder for P1: carries the verified Unity-source constants once the
/// heightfield simulation is ported. Kept empty of state so the scaffold builds.
/// </summary>
public sealed class SimulationConfig
{
    public const int GridSize = 129;
    public const float WorldExtent = 96f;
    public const float CellSize = WorldExtent / (GridSize - 1);
}
