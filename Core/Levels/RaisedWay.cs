using TheLevels.Core.Math;

namespace TheLevels.Core.Levels
{
    /// <summary>
    /// Level 1, "The Raised Way": the Somerset flood basin the tribe bridges to reach the
    /// First May Pike. Verbatim move of HeightfieldSimulation.GenerateSomersetTestMap —
    /// same noise, mounds, rhyne and pools — so the default world is bit-identical.
    /// A new level is a new file here plus one argument at the HeightfieldSimulation
    /// construction site; it never opens the solver.
    /// ponytail: a level is a C# fill function, not a resource format. Add a heightmap
    /// importer when hand-authoring terrain in code actually hurts.
    /// </summary>
    public static class RaisedWay
    {
        public static void Fill(int resolution, float worldSize, float minHeight, float maxHeight,
            float[] terrain, float[] water)
        {
            float cellSize = worldSize / (resolution - 1);
            float half = worldSize * 0.5f;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                float worldX = x * cellSize - half;
                float worldZ = z * cellSize - half;
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

                int index = x + z * resolution;
                terrain[index] = SimulationMath.Clamp(height, minHeight, maxHeight);

                bool upperPool = SimulationMath.Distance(worldX, worldZ, -3f, 27f) < 11f;
                bool lowerBasin = SimulationMath.Distance(worldX, worldZ, 17f, -29f) < 13f;
                bool rhyne = SimulationMath.Abs(worldX - channelCenter) < 2.1f && worldZ > -25f && worldZ < 25f;
                float targetSurface = upperPool ? 3.0f : lowerBasin ? 1.7f : rhyne ? 1.55f : 0f;
                water[index] = SimulationMath.Max(0f, targetSurface - terrain[index]);
            }
        }

        private static float Gaussian(float x, float z, float centerX, float centerZ, float radius, float height)
        {
            float dx = x - centerX;
            float dz = z - centerZ;
            return SimulationMath.Exp(-(dx * dx + dz * dz) / (2f * radius * radius)) * height;
        }
    }
}
