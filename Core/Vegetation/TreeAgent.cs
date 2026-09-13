using System;
using System.Numerics;
using TheLevels.Core.Simulation;

namespace TheLevels.Core.Vegetation;

public enum TreeSpecies { Oak, Rowan, Willow }

public enum TreeState { Alive, Burning, Charred, Drowned }

/// <summary>
/// One tree: static, but alive to the world — it grows, catches fire from the fuel
/// grid at its cell, torches its grove neighbours while it burns, chars to a standing
/// snag, and sinks if the ground under it floods. Engine-free; <c>View/FloraView.cs</c>
/// gives it a low-poly trunk and canopy through a MultiMesh.
/// </summary>
public sealed class TreeAgent
{
    public const float GrowthSeconds = 90f;      // sapling (0.35) to mature (1)
    public const float SaplingGrowth = 0.35f;
    public const float CatchFire = 0.12f;        // cell fire intensity that ignites it
    public const float BurnSeconds = 6f;
    public const float SpreadInterval = 0.5f;    // torch pulse cadence while burning
    public const float SpreadRadius = 3.4f;      // must clear ForestManager.MinSpacing
    public const float DrownDepth = 0.55f;
    public const float DrownAfterSeconds = 5f;

    private readonly HeightfieldSimulation heightfield;
    private readonly FireSimulation fire;
    private float drownTimer;
    private float spreadTimer;

    public TreeAgent(Vector3 position, TreeSpecies species, float scale, float rotationY,
        float tint, HeightfieldSimulation heightfield, FireSimulation fire)
    {
        Position = position;
        Species = species;
        Scale = scale;
        RotationY = rotationY;
        Tint = tint;
        this.heightfield = heightfield;
        this.fire = fire;
    }

    public Vector3 Position { get; }            // sim space; Y is the spawn-time ground
    public TreeSpecies Species { get; }
    public float Scale { get; }                 // seeded 0.85..1.15 size variation
    public float RotationY { get; }             // seeded, radians
    public float Tint { get; }                  // seeded 0.85..1.10 canopy colour variation
    public float Growth { get; private set; } = SaplingGrowth;
    public TreeState State { get; private set; } = TreeState.Alive;
    public float BurnTimer { get; private set; }
    public float Sink { get; private set; }     // 0..1 drowned sink progress for the view

    public bool IsStanding => State != TreeState.Drowned;

    public void Tick(float delta)
    {
        switch (State)
        {
            case TreeState.Alive:
                Growth = MathF.Min(1f, Growth + delta / GrowthSeconds);
                heightfield.WorldToGrid(Position, out float gx, out float gz);
                if (fire.GetFire((int)gx, (int)gz) > CatchFire)
                {
                    State = TreeState.Burning;
                    break;
                }
                if (heightfield.SampleWater(Position) > DrownDepth)
                {
                    drownTimer += delta;
                    if (drownTimer > DrownAfterSeconds) State = TreeState.Drowned;
                }
                else drownTimer = MathF.Max(0f, drownTimer - delta * 2f);
                break;

            case TreeState.Burning:
                BurnTimer += delta;
                spreadTimer -= delta;
                if (spreadTimer <= 0f)
                {
                    // A burning tree is a fire source: kindle every fuelled dry cell
                    // around it, which is how the fire jumps tree to tree in a grove.
                    fire.Ignite(Position, SpreadRadius, 0.5f);
                    spreadTimer = SpreadInterval;
                }
                if (BurnTimer > BurnSeconds) State = TreeState.Charred;
                break;

            case TreeState.Drowned:
                Sink = MathF.Min(1f, Sink + delta / 3f);
                break;
        }
    }
}
