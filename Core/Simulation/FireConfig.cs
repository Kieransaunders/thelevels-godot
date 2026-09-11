namespace TheLevels.Core.Simulation;

/// <summary>
/// Tunable parameters of the fire simulation. Defaults reproduce the Unity
/// prototype's serialized inspector values exactly.
/// </summary>
public sealed class FireConfig
{
    public float FireStep = 0.1f;             // seconds
    public int MaxSubstepsPerFrame = 2;
    public float WetDepth = 0.02f;
    public float IgniteFuelThreshold = 0.08f;
    public float SpreadRate = 0.6f;
    public float BurnRate = 0.10f;
    public float EmberRate = 40f;
    public float EmberCapacity = 80f;
    public int RandomSeed = 1337;
}
