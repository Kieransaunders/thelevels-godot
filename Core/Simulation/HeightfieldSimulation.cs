using System;
using System.Diagnostics;
using System.Numerics;
using TheLevels.Core.Math;

namespace TheLevels.Core.Simulation
{
    /// <summary>
    /// Deformable-earth / shallow-water heightfield simulation. Direct port of the Unity
    /// prototype's HeightfieldSimulation MonoBehaviour (2026-09-07 source baseline);
    /// simulation coordinates and float math are unchanged. Engine-facing behaviour
    /// (Update ticking, logging) is replaced by explicit calls and error state.
    /// </summary>
    public sealed class HeightfieldSimulation
    {
        private readonly SimulationConfig config;

        private const float Gravity = 9.81f;

        private float[] terrain = Array.Empty<float>();
        private float[] water = Array.Empty<float>();
        private float[] initialTerrain = Array.Empty<float>();
        private float[] initialWater = Array.Empty<float>();
        // Staggered velocity field: velX[c] is the flux velocity on the edge from cell c to c+1 in x,
        // velZ[c] the edge from c to c+1 in z. Shallow-water (Müller) solver.
        private float[] velX = Array.Empty<float>();
        private float[] velZ = Array.Empty<float>();
        private float[] outgoing = Array.Empty<float>();
        private float[] delta = Array.Empty<float>();
        private float accumulator;
        private int stepCount;
        private readonly Stopwatch stepWatch = new Stopwatch();

        public event Action? StateChanged;

        /// <summary>Raised, once, when the solver pauses itself after an invalid water value.</summary>
        public event Action<string>? Faulted;

        /// <summary>Non-empty after the solver paused itself; cleared by Initialize/ResetSimulation.</summary>
        public string? LastError { get; private set; }

        public HeightfieldSimulation() : this(new SimulationConfig()) { }

        public HeightfieldSimulation(SimulationConfig simulationConfig)
        {
            config = simulationConfig;
            Initialize();
        }

        public int Resolution { get; private set; }
        public float WorldSize => config.WorldSize;
        public float CellSize => WorldSize / (Resolution - 1);
        public float CellArea => CellSize * CellSize;
        public float BrushRadius => config.BrushRadius;
        public float EarthCapacity => config.EarthCapacity;
        public float WaterCapacity => config.WaterCapacity;
        public float EarthBuffer { get; private set; }
        public float WaterBuffer { get; private set; }
        public bool Paused { get; set; }
        public int StepCount => stepCount;
        public float FixedStep => config.SimulationStep;
        public float LastStepMilliseconds { get; private set; }
        public float MaxWaterSpeed { get; private set; }
        public int NegativeCorrections { get; private set; }
        public int InvalidValueCount { get; private set; }
        public int HeightClampCount { get; private set; }

        /// <summary>
        /// Engine-independent replacement for MonoBehaviour.Update: accumulates capped elapsed
        /// time and runs up to MaxSubstepsPerFrame fixed water steps. Ignored while paused.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (Paused)
                return;

            accumulator += SimulationMath.Min(deltaTime, config.SimulationStep * config.MaxSubstepsPerFrame);
            int substeps = 0;
            while (accumulator >= config.SimulationStep && substeps < config.MaxSubstepsPerFrame)
            {
                StepWater(config.SimulationStep);
                accumulator -= config.SimulationStep;
                substeps++;
            }
        }

        public void Initialize()
        {
            Resolution = SimulationMath.Max(33, config.Resolution | 1);
            config.Resolution = Resolution;
            int count = Resolution * Resolution;
            terrain = new float[count];
            water = new float[count];
            initialTerrain = new float[count];
            initialWater = new float[count];
            velX = new float[count];
            velZ = new float[count];
            outgoing = new float[count];
            delta = new float[count];

            GenerateSomersetTestMap();
            Array.Copy(terrain, initialTerrain, count);
            Array.Copy(water, initialWater, count);
            ResetSimulation();
        }

        public void ResetSimulation()
        {
            Array.Copy(initialTerrain, terrain, terrain.Length);
            Array.Copy(initialWater, water, water.Length);
            Array.Clear(velX, 0, velX.Length);
            Array.Clear(velZ, 0, velZ.Length);
            Array.Clear(outgoing, 0, outgoing.Length);
            Array.Clear(delta, 0, delta.Length);
            EarthBuffer = 0f;
            WaterBuffer = 0f;
            accumulator = 0f;
            stepCount = 0;
            NegativeCorrections = 0;
            InvalidValueCount = 0;
            HeightClampCount = 0;
            LastStepMilliseconds = 0f;
            MaxWaterSpeed = 0f;
            LastError = null;
            Paused = false;
            StateChanged?.Invoke();
        }

        public void StepOnce()
        {
            StepWater(config.SimulationStep);
        }

        public float SampleTerrain(Vector3 worldPosition) => SampleBilinear(terrain, worldPosition);
        public float SampleWater(Vector3 worldPosition) => SampleBilinear(water, worldPosition);

        public float SampleSurface(Vector3 worldPosition)
        {
            return SampleTerrain(worldPosition) + SampleWater(worldPosition);
        }

        public bool ContainsWorldPosition(Vector3 position)
        {
            float half = WorldSize * 0.5f;
            return position.X >= -half && position.X <= half && position.Z >= -half && position.Z <= half;
        }

        public float GetTerrain(int x, int z) => terrain[Index(x, z)];
        public float GetWater(int x, int z) => water[Index(x, z)];

        /// <summary>
        /// Mean flow speed (m/s) over the four staggered solver edges around a cell —
        /// a read-only presentation accessor for whitewater, not a solver input. Edges
        /// past the domain border count as zero (they are reflective in the solver).
        /// </summary>
        public float FlowSpeed(int x, int z)
        {
            float vx = 0f, vz = 0f, edges = 0f;
            if (x > 0) { vx += velX[Index(x - 1, z)]; edges++; }
            if (x < Resolution - 1) { vx += velX[Index(x, z)]; edges++; }
            if (z > 0) { vz += velZ[Index(x, z - 1)]; edges++; }
            if (z < Resolution - 1) { vz += velZ[Index(x, z)]; edges++; }
            if (edges == 0f) return 0f;
            vx /= edges;
            vz /= edges;
            return SimulationMath.Sqrt(vx * vx + vz * vz);
        }

        public float ApplyBrush(Vector3 worldPosition, MatterType matter, bool scoop, float deltaTime)
        {
            if (!ContainsWorldPosition(worldPosition) || deltaTime <= 0f)
                return 0f;

            float availableBuffer = matter == MatterType.Earth ? EarthBuffer : WaterBuffer;
            float remainingCapacity = matter == MatterType.Earth
                ? config.EarthCapacity - EarthBuffer
                : config.WaterCapacity - WaterBuffer;
            float requestedVolume = config.TransferRate * deltaTime;
            requestedVolume = scoop
                ? SimulationMath.Min(requestedVolume, remainingCapacity)
                : SimulationMath.Min(requestedVolume, availableBuffer);

            if (requestedVolume <= 0.00001f)
                return 0f;

            WorldToGrid(worldPosition, out float gridX, out float gridZ);
            float radiusCells = config.BrushRadius / CellSize;
            int minX = SimulationMath.Max(0, SimulationMath.FloorToInt(gridX - radiusCells));
            int maxX = SimulationMath.Min(Resolution - 1, SimulationMath.CeilToInt(gridX + radiusCells));
            int minZ = SimulationMath.Max(0, SimulationMath.FloorToInt(gridZ - radiusCells));
            int maxZ = SimulationMath.Min(Resolution - 1, SimulationMath.CeilToInt(gridZ + radiusCells));

            float totalWeight = 0f;
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                float distance = SimulationMath.Distance(x, z, gridX, gridZ);
                if (distance <= radiusCells)
                    totalWeight += SmoothBrushWeight(distance / radiusCells);
            }

            if (totalWeight <= 0f)
                return 0f;

            float transferred = 0f;
            float targetDepthPerWeight = requestedVolume / (CellArea * totalWeight);
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                float distance = SimulationMath.Distance(x, z, gridX, gridZ);
                if (distance > radiusCells)
                    continue;

                float depth = targetDepthPerWeight * SmoothBrushWeight(distance / radiusCells);
                int index = Index(x, z);
                float appliedDepth;

                if (matter == MatterType.Earth)
                {
                    if (scoop)
                    {
                        appliedDepth = SimulationMath.Min(depth, terrain[index] - config.MinimumTerrainHeight);
                        appliedDepth = SimulationMath.Max(0f, appliedDepth);
                        terrain[index] -= appliedDepth;
                        // ponytail: high water table — hollows dug below groundwaterLevel seep
                        // full of water. Springs water from nothing; fine for a prototype.
                        float surface = terrain[index] + water[index];
                        if (surface < config.GroundwaterLevel)
                            water[index] += config.GroundwaterLevel - surface;
                    }
                    else
                    {
                        appliedDepth = SimulationMath.Min(depth, config.MaximumTerrainHeight - terrain[index]);
                        appliedDepth = SimulationMath.Max(0f, appliedDepth);
                        terrain[index] += appliedDepth;
                    }
                }
                else
                {
                    if (scoop)
                    {
                        appliedDepth = SimulationMath.Min(depth, water[index]);
                        water[index] -= appliedDepth;
                    }
                    else
                    {
                        appliedDepth = depth;
                        water[index] += appliedDepth;
                    }
                }

                transferred += appliedDepth * CellArea;
            }

            if (matter == MatterType.Earth)
                EarthBuffer = SimulationMath.Clamp(EarthBuffer + (scoop ? transferred : -transferred), 0f, config.EarthCapacity);
            else
                WaterBuffer = SimulationMath.Clamp(WaterBuffer + (scoop ? transferred : -transferred), 0f, config.WaterCapacity);

            if (transferred > 0f)
                StateChanged?.Invoke();
            return transferred;
        }

        public SimulationMetrics CalculateMetrics()
        {
            double terrainVolume = 0d;
            double waterVolume = 0d;
            float maxWater = 0f;
            int wetCells = 0;

            for (int i = 0; i < terrain.Length; i++)
            {
                terrainVolume += terrain[i] * CellArea;
                waterVolume += water[i] * CellArea;
                if (water[i] > config.DryEpsilon)
                {
                    wetCells++;
                    maxWater = SimulationMath.Max(maxWater, water[i]);
                }
            }

            return new SimulationMetrics
            {
                TerrainVolume = terrainVolume,
                WaterVolume = waterVolume,
                WetCells = wetCells,
                MaximumWaterDepth = maxWater
            };
        }

        private void StepWater(float dt)
        {
            stepWatch.Restart();
            float dx = CellSize;
            float invDx = 1f / dx;
            float velLimit = dx / dt * 0.5f; // CFL: at most half a cell per step.

            // --- Velocity integration: accelerate down the surface (terrain+water) gradient. ---
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                int c = Index(x, z);
                float surfC = Surface(c);

                if (x < Resolution - 1)
                {
                    int r = Index(x + 1, z);
                    float surfR = Surface(r);
                    float v = velX[c] - Gravity * invDx * (surfR - surfC) * dt;
                    // Boundary: a dry cell whose ground stands above the neighbour's surface is a wall.
                    bool blocked = (water[c] <= config.DryEpsilon && terrain[c] > surfR)
                                || (water[r] <= config.DryEpsilon && terrain[r] > surfC);
                    velX[c] = blocked ? 0f : SimulationMath.Clamp(v, -velLimit, velLimit);
                }
                else velX[c] = 0f; // reflective domain edge

                if (z < Resolution - 1)
                {
                    int b = Index(x, z + 1);
                    float surfB = Surface(b);
                    float v = velZ[c] - Gravity * invDx * (surfB - surfC) * dt;
                    bool blocked = (water[c] <= config.DryEpsilon && terrain[c] > surfB)
                                || (water[b] <= config.DryEpsilon && terrain[b] > surfC);
                    velZ[c] = blocked ? 0f : SimulationMath.Clamp(v, -velLimit, velLimit);
                }
                else velZ[c] = 0f;
            }

            // --- Height integration: move h_upwind * v across each edge (flux form). ---
            // ponytail: skipped the paper's overshoot-reduction term — it only bites above
            // ~4.6 m average depth, which this world never reaches. Add if depths grow.
            // (Documented port limitation, not a verified safety property at arbitrary depths.)
            // Pass 1: tally each cell's total outflow so we can cap it at the water it holds.
            Array.Clear(outgoing, 0, outgoing.Length);
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution - 1; x++)
            {
                int c = Index(x, z);
                float v = velX[c];
                float transfer = (v >= 0f ? water[c] : water[Index(x + 1, z)]) * v * invDx * dt;
                outgoing[transfer >= 0f ? c : Index(x + 1, z)] += SimulationMath.Abs(transfer);
            }
            for (int z = 0; z < Resolution - 1; z++)
            for (int x = 0; x < Resolution; x++)
            {
                int c = Index(x, z);
                float v = velZ[c];
                float transfer = (v >= 0f ? water[c] : water[Index(x, z + 1)]) * v * invDx * dt;
                outgoing[transfer >= 0f ? c : Index(x, z + 1)] += SimulationMath.Abs(transfer);
            }

            // Pass 2: apply each edge, scaling by the donor's cap so no cell goes negative.
            Array.Clear(delta, 0, delta.Length);
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution - 1; x++)
            {
                int c = Index(x, z);
                int r = Index(x + 1, z);
                float v = velX[c];
                float transfer = (v >= 0f ? water[c] : water[r]) * v * invDx * dt;
                transfer *= DonorScale(transfer >= 0f ? c : r);
                delta[c] -= transfer;
                delta[r] += transfer;
            }
            for (int z = 0; z < Resolution - 1; z++)
            for (int x = 0; x < Resolution; x++)
            {
                int c = Index(x, z);
                int b = Index(x, z + 1);
                float v = velZ[c];
                float transfer = (v >= 0f ? water[c] : water[b]) * v * invDx * dt;
                transfer *= DonorScale(transfer >= 0f ? c : b);
                delta[c] -= transfer;
                delta[b] += transfer;
            }

            bool changed = false;
            for (int i = 0; i < water.Length; i++)
            {
                float next = water[i] + delta[i];
                if (SimulationMath.IsNaN(next) || SimulationMath.IsInfinity(next))
                {
                    InvalidValueCount++;
                    Paused = true;
                    LastError = $"The Levels simulation paused after detecting an invalid water value at cell {i}.";
                    Faulted?.Invoke(LastError);
                    return;
                }

                if (next < 0f)
                {
                    if (next < -0.00001f)
                        NegativeCorrections++;
                    next = 0f;
                }

                if (next < config.DryEpsilon)
                    next = 0f;
                if (next == 0f)
                    velX[i] = velZ[i] = 0f; // don't let a dried cell carry stale momentum
                changed |= !SimulationMath.Approximately(next, water[i]);
                water[i] = next;
            }

            float peak = 0f;
            for (int i = 0; i < water.Length; i++)
                if (water[i] > config.DryEpsilon)
                    peak = SimulationMath.Max(peak, velX[i] * velX[i] + velZ[i] * velZ[i]);
            MaxWaterSpeed = SimulationMath.Sqrt(peak);

            stepCount++;
            LastStepMilliseconds = (float)stepWatch.Elapsed.TotalMilliseconds;
            if (changed)
                StateChanged?.Invoke();
        }

        private void GenerateSomersetTestMap()
        {
            float half = WorldSize * 0.5f;
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                float worldX = x * CellSize - half;
                float worldZ = z * CellSize - half;
                // Unity baseline used Mathf.PerlinNoise here; replaced by deterministic value
                // noise (accepted deviation) at the same 0.055 scale and 0.45 amplitude.
                float lowUndulation = DeterministicNoise.ValueNoise(x * 0.055f, z * 0.055f) * 0.45f;
                float height = 1.1f + lowUndulation;

                height += Gaussian(worldX, worldZ, -8f, -4f, 19f, 4.2f);
                height += Gaussian(worldX, worldZ, 20f, -12f, 11f, 2.4f);
                height += Gaussian(worldX, worldZ, -27f, 13f, 9f, 2.0f);
                height += Gaussian(worldX, worldZ, 25f, 31f, 10f, 11.5f);

                float channelCenter = SimulationMath.Sin((worldZ + 15f) * 0.09f) * 8f + 4f;
                float channel = SimulationMath.Exp(-SimulationMath.Pow((worldX - channelCenter) / 3.2f, 2f));
                height -= channel * 0.8f;

                int index = Index(x, z);
                terrain[index] = SimulationMath.Clamp(height, config.MinimumTerrainHeight, config.MaximumTerrainHeight);

                bool upperPool = SimulationMath.Distance(worldX, worldZ, -3f, 27f) < 11f;
                bool lowerBasin = SimulationMath.Distance(worldX, worldZ, 17f, -29f) < 13f;
                bool rhyne = SimulationMath.Abs(worldX - channelCenter) < 2.1f && worldZ > -25f && worldZ < 25f;
                float targetSurface = upperPool ? 3.0f : lowerBasin ? 1.7f : rhyne ? 1.55f : 0f;
                water[index] = SimulationMath.Max(0f, targetSurface - terrain[index]);
            }
        }

        private float SampleBilinear(float[] values, Vector3 worldPosition)
        {
            WorldToGrid(worldPosition, out float gx, out float gz);
            int x0 = SimulationMath.Clamp(SimulationMath.FloorToInt(gx), 0, Resolution - 1);
            int z0 = SimulationMath.Clamp(SimulationMath.FloorToInt(gz), 0, Resolution - 1);
            int x1 = SimulationMath.Min(x0 + 1, Resolution - 1);
            int z1 = SimulationMath.Min(z0 + 1, Resolution - 1);
            float tx = gx - x0;
            float tz = gz - z0;
            float a = SimulationMath.Lerp(values[Index(x0, z0)], values[Index(x1, z0)], tx);
            float b = SimulationMath.Lerp(values[Index(x0, z1)], values[Index(x1, z1)], tx);
            return SimulationMath.Lerp(a, b, tz);
        }

        public void WorldToGrid(Vector3 position, out float x, out float z)
        {
            float half = WorldSize * 0.5f;
            x = SimulationMath.Clamp((position.X + half) / CellSize, 0f, Resolution - 1f);
            z = SimulationMath.Clamp((position.Z + half) / CellSize, 0f, Resolution - 1f);
        }

        private float Surface(int index) => terrain[index] + water[index];
        private int Index(int x, int z) => x + z * Resolution;

        // Scale a donor cell's outflow so the total never exceeds the water it holds.
        private float DonorScale(int donor)
            => outgoing[donor] > water[donor] && outgoing[donor] > 0f
                ? water[donor] / outgoing[donor]
                : 1f;

        private static float SmoothBrushWeight(float normalizedDistance)
        {
            float t = SimulationMath.Clamp01(1f - normalizedDistance);
            return t * t * (3f - 2f * t);
        }

        private static float Gaussian(float x, float z, float centerX, float centerZ, float radius, float height)
        {
            float dx = x - centerX;
            float dz = z - centerZ;
            return SimulationMath.Exp(-(dx * dx + dz * dz) / (2f * radius * radius)) * height;
        }
    }
}
