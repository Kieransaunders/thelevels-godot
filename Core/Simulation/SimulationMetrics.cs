namespace TheLevels.Core.Simulation;

public enum MatterType
{
    Earth,
    Water
}

public struct SimulationMetrics
{
    public double TerrainVolume;
    public double WaterVolume;
    public int WetCells;
    public float MaximumWaterDepth;
}
