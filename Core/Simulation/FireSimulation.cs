using System;
using System.Numerics;
using TheLevels.Core.Math;

namespace TheLevels.Core.Simulation
{
    /// Fire on the same grid as the heightfield: reed-bed fuel that ignites, spreads to
    /// dry neighbours, is extinguished by water, and leaves charred ground behind.
    /// The druid can scoop fire into an ember buffer and drop it elsewhere, or call
    /// lightning to kindle it. Shares pause/step ownership with the water solver.
    /// Direct port of the Unity prototype's FireSimulation MonoBehaviour (2026-09-07
    /// baseline); engine-facing Update became Advance(deltaTime).
    public sealed class FireSimulation
    {
        private readonly HeightfieldSimulation heightfield;
        private readonly FireConfig config;

        private float[] fuel = Array.Empty<float>();
        private float[] fire = Array.Empty<float>(); // 0..1 burning intensity
        private bool[] charred = Array.Empty<bool>();
        private Random rng;
        private float accumulator;
        private int stepCount;

        public FireSimulation(HeightfieldSimulation heightfieldSimulation) : this(heightfieldSimulation, new FireConfig()) { }

        public FireSimulation(HeightfieldSimulation heightfieldSimulation, FireConfig fireConfig)
        {
            heightfield = heightfieldSimulation;
            config = fireConfig;
            rng = new Random(config.RandomSeed);
            Initialize();
        }

        public event Action? StateChanged;

        public int Resolution => heightfield.Resolution;
        public float EmberBuffer { get; private set; }
        public float EmberCapacity => config.EmberCapacity;
        public int BurningCells { get; private set; }
        public int CharredCells { get; private set; }
        public int StepCount => stepCount;
        public bool Paused { get; set; }

        /// <summary>
        /// Engine-independent replacement for MonoBehaviour.Update: automatic stepping
        /// stops when paused (itself or the water solver) or when nothing burns; the
        /// elapsed frame time is capped at maxSubsteps * fireStep.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (Paused || heightfield.Paused || BurningCells == 0)
                return;

            accumulator += SimulationMath.Min(deltaTime, config.FireStep * config.MaxSubstepsPerFrame);
            int substeps = 0;
            while (accumulator >= config.FireStep && substeps < config.MaxSubstepsPerFrame)
            {
                StepOnce();
                accumulator -= config.FireStep;
                substeps++;
            }
        }

        public void Initialize()
        {
            int count = heightfield.Resolution * heightfield.Resolution;
            fuel = new float[count];
            fire = new float[count];
            charred = new bool[count];
            rng = new Random(config.RandomSeed);
            GenerateFuel();
            ResetFire();
        }

        public void ResetFire()
        {
            Array.Clear(fire, 0, fire.Length);
            GenerateFuel();
            EmberBuffer = 0f;
            accumulator = 0f;
            stepCount = 0;
            BurningCells = 0;
            CharredCells = 0;
            Paused = false;
            StateChanged?.Invoke();
        }

        public float GetFuel(int x, int z) => fuel[x + z * Resolution];
        public float GetFire(int x, int z) => fire[x + z * Resolution];
        public bool GetCharred(int x, int z) => charred[x + z * Resolution];

        /// Kindle every dry, fuelled cell within radius. Returns how many caught.
        public int Ignite(Vector3 worldPosition, float radius, float kindling = 0.35f)
        {
            if (!heightfield.ContainsWorldPosition(worldPosition))
                return 0;

            int ignited = 0;
            ForEachCellInDisc(worldPosition, radius, (x, z, weight) =>
            {
                int i = x + z * Resolution;
                if (fire[i] > 0f || fuel[i] < config.IgniteFuelThreshold || heightfield.GetWater(x, z) > config.WetDepth)
                    return;
                fire[i] = kindling * SimulationMath.Max(0.35f, weight);
                ignited++;
            });
            if (ignited > 0)
            {
                CountFires();
                StateChanged?.Invoke();
            }
            return ignited;
        }

        /// A lightning strike: a hotter kindling than the ember brush.
        public int Strike(Vector3 worldPosition, float radius) => Ignite(worldPosition, radius, 0.9f);

        /// Druid fire brush: scoop pulls burning cells into the ember buffer,
        /// drop spends embers to kindle the brush area. Returns units moved.
        public float ApplyFireBrush(Vector3 worldPosition, bool scoop, float deltaTime)
        {
            if (!heightfield.ContainsWorldPosition(worldPosition) || deltaTime <= 0f)
                return 0f;

            if (scoop)
            {
                float capacity = config.EmberCapacity - EmberBuffer;
                float budget = SimulationMath.Min(config.EmberRate * deltaTime, capacity);
                if (budget <= 0.0001f)
                    return 0f;

                float scooped = 0f;
                ForEachCellInDisc(worldPosition, heightfield.BrushRadius, (x, z, weight) =>
                {
                    if (scooped >= budget)
                        return;
                    // Drain whole cells until the budget runs out — the druid vacuums
                    // flames, rather than shaving a fraction off each cell forever.
                    int i = x + z * Resolution;
                    float take = SimulationMath.Min(fire[i], budget - scooped);
                    fire[i] -= take;
                    scooped += take;
                });
                // Clamp any cell the smooth falloff rounded to a sliver.
                for (int i = 0; i < fire.Length; i++)
                    if (fire[i] < 0.01f)
                        fire[i] = 0f;

                if (scooped > 0f)
                {
                    EmberBuffer += scooped;
                    CountFires();
                    StateChanged?.Invoke();
                }
                return scooped;
            }
            else
            {
                float budget = SimulationMath.Min(config.EmberRate * deltaTime, EmberBuffer);
                if (budget <= 0.0001f)
                    return 0f;

                float spent = 0f;
                ForEachCellInDisc(worldPosition, heightfield.BrushRadius, (x, z, weight) =>
                {
                    if (spent >= budget)
                        return;
                    int i = x + z * Resolution;
                    if (fire[i] > 0f || fuel[i] < config.IgniteFuelThreshold || heightfield.GetWater(x, z) > config.WetDepth)
                        return;
                    // Source defect preserved: one cell costs 0.5 embers even when the
                    // remaining budget is below 0.5, so `spent` can overshoot `budget`.
                    // Ported unchanged; correcting it needs a separate change + regression test.
                    float cost = 0.5f; // one ember unit kindles one cell
                    fire[i] = 0.5f * SimulationMath.Max(0.4f, weight);
                    spent += cost;
                });

                if (spent > 0f)
                {
                    EmberBuffer -= spent;
                    CountFires();
                    StateChanged?.Invoke();
                }
                return spent;
            }
        }

        public void StepOnce()
        {
            if (fuel.Length == 0)
                return;

            int res = Resolution;
            bool changed = false;

            // Burn, grow, extinguish, char.
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                int i = x + z * res;
                if (fire[i] <= 0f)
                    continue;

                if (heightfield.GetWater(x, z) > config.WetDepth || fuel[i] <= 0f)
                {
                    fire[i] = SimulationMath.Max(0f, fire[i] - 2.5f * config.FireStep);
                    changed = true;
                    continue;
                }

                fire[i] = SimulationMath.Min(1f, fire[i] + 2f * config.FireStep);
                fuel[i] -= config.BurnRate * config.FireStep * fire[i];
                if (fuel[i] <= 0.001f)
                {
                    fuel[i] = 0f;
                    fire[i] = 0f;
                    charred[i] = true;
                }
                changed = true;
            }

            // Spread from established flames to dry, fuelled 4-neighbours (diagonals half as likely).
            if (BurningCells > 0)
            {
                int[] spreadOrder = new int[res * res];
                int n = 0;
                for (int i = 0; i < fire.Length; i++)
                    if (fire[i] > 0.3f)
                        spreadOrder[n++] = i;

                for (int s = 0; s < n; s++)
                {
                    int i = spreadOrder[s];
                    int x = i % res;
                    int z = i / res;
                    float chance = config.SpreadRate * config.FireStep * fire[i];

                    TrySpread(x + 1, z, chance, ref changed);
                    TrySpread(x - 1, z, chance, ref changed);
                    TrySpread(x, z + 1, chance, ref changed);
                    TrySpread(x, z - 1, chance, ref changed);
                    TrySpread(x + 1, z + 1, chance * 0.5f, ref changed);
                    TrySpread(x + 1, z - 1, chance * 0.5f, ref changed);
                    TrySpread(x - 1, z + 1, chance * 0.5f, ref changed);
                    TrySpread(x - 1, z - 1, chance * 0.5f, ref changed);
                }
            }

            stepCount++;
            if (changed)
            {
                CountFires();
                StateChanged?.Invoke();
            }
        }

        private void TrySpread(int x, int z, float chance, ref bool changed)
        {
            if (x < 0 || z < 0 || x >= Resolution || z >= Resolution)
                return;

            int i = x + z * Resolution;
            if (fire[i] > 0f || fuel[i] < config.IgniteFuelThreshold || heightfield.GetWater(x, z) > config.WetDepth)
                return;

            if ((float)rng.NextDouble() < chance * (0.35f + 0.65f * fuel[i]))
            {
                fire[i] = 0.2f;
                changed = true;
            }
        }

        private void CountFires()
        {
            int burning = 0, charredCount = 0;
            for (int i = 0; i < fire.Length; i++)
            {
                if (fire[i] > 0f) burning++;
                if (charred[i]) charredCount++;
            }
            BurningCells = burning;
            CharredCells = charredCount;
        }

        private void GenerateFuel()
        {
            int res = Resolution;
            float half = heightfield.WorldSize * 0.5f;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float height = heightfield.GetTerrain(x, z);
                float worldX = x * heightfield.CellSize - half;
                float worldZ = z * heightfield.CellSize - half;

                // Reed beds on the dry summer levels, thicker scrub on the moor edges,
                // bare rock on the tor tops. Note: SmoothStep interpolates over
                // [from, to], so ramp through InverseLerp first to get a 0..1 factor.
                // (Unity baseline used Mathf.PerlinNoise here; replaced by deterministic
                // value noise — accepted deviation at the same scale and amplitude.)
                float reeds = Band(height, 1.7f, 1.6f) * 0.62f;
                float scrub = Band(height, 4.2f, 3.2f) * 0.85f;
                float rock = SimulationMath.SmoothStep(0f, 1f, SimulationMath.InverseLerp(9f, 12.5f, height));
                float noise = 0.4f + 0.6f * DeterministicNoise.ValueNoise(worldX * 0.09f + 100f, worldZ * 0.09f - 60f);
                fuel[x + z * res] = SimulationMath.Clamp01((reeds + scrub) * (1f - rock)) * noise;
                charred[x + z * res] = false;
            }
        }

        private static float Band(float value, float center, float width)
            => SimulationMath.Clamp01(1f - SimulationMath.Abs(value - center) / width);

        private void ForEachCellInDisc(Vector3 worldPosition, float radius, Action<int, int, float> visit)
        {
            float half = heightfield.WorldSize * 0.5f;
            float cell = heightfield.CellSize;
            float gx = (worldPosition.X + half) / cell;
            float gz = (worldPosition.Z + half) / cell;
            float radiusCells = SimulationMath.Max(1f, radius / cell);
            int minX = SimulationMath.Max(0, SimulationMath.FloorToInt(gx - radiusCells));
            int maxX = SimulationMath.Min(Resolution - 1, SimulationMath.CeilToInt(gx + radiusCells));
            int minZ = SimulationMath.Max(0, SimulationMath.FloorToInt(gz - radiusCells));
            int maxZ = SimulationMath.Min(Resolution - 1, SimulationMath.CeilToInt(gz + radiusCells));

            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                float distance = SimulationMath.Distance(x, z, gx, gz);
                if (distance > radiusCells)
                    continue;
                float t = SimulationMath.Clamp01(1f - distance / radiusCells);
                visit(x, z, t * t * (3f - 2f * t));
            }
        }
    }
}
