namespace TheLevels.Core.Simulation;

/// <summary>
/// Tunable parameters of the heightfield simulation. Defaults reproduce the Unity
/// prototype's serialized inspector values exactly; these are the port's baseline.
/// </summary>
public sealed class SimulationConfig
{
    // World
    public int Resolution = 129;               // Unity Range(33, 257), odd enforced in Initialize
    public float WorldSize = 96f;              // metres
    public float MinimumTerrainHeight = 0.2f;
    public float MaximumTerrainHeight = 18f;

    // Water
    public float SimulationStep = 1f / 30f;    // seconds
    public int MaxSubstepsPerFrame = 4;
    public float DryEpsilon = 0.0005f;

    // Matter tools
    public float BrushRadius = 3.5f;
    public float TransferRate = 20f;           // m³/s
    public float EarthCapacity = 140f;         // m³
    public float WaterCapacity = 140f;         // m³
    public float GroundwaterLevel = 1.2f;

    public float CellSize => WorldSize / (Resolution - 1);
    public float CellArea => CellSize * CellSize;
}
