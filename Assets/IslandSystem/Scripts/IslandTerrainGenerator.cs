using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Pure terrain construction from a <see cref="BiomeAssetManifest"/>: heightmap (multi-octave Perlin
    /// masked into an island shape), splatmap (height + slope rules) and prop scatter.
    /// Stateless on purpose so the same biome + different seeds give different islands.
    /// </summary>
    public static class IslandTerrainGenerator
    {
        // ---- Heightmap ----------------------------------------------------

        /// <summary>Normalized (0..1) multi-octave Perlin sample.</summary>
        public static float SampleHeight(int x, int y, int size, NoiseSettings n, float seedOffset)
        {
            float h = 0f, amp = n.amplitude, freq = n.frequency, norm = 0f;
            int octaves = Mathf.Max(1, n.octaves);
            for (int o = 0; o < octaves; o++)
            {
                h += Mathf.PerlinNoise(
                        (float)x / size * freq + seedOffset,
                        (float)y / size * freq + seedOffset) * amp;
                norm += amp;
                freq *= 2f;
                amp *= 0.5f;
            }
            return norm > 0f ? h / norm : h;
        }

        /// <summary>1 at the center, fading to 0 at the edges so the land sinks into the sea.</summary>
        public static float IslandMask(int x, int y, int size, float falloff)
        {
            float dx = (float)x / size - 0.5f;
            float dy = (float)y / size - 0.5f;
            float dist = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 0 center, ~1 at edge midpoints
            return Mathf.Pow(Mathf.Clamp01(1f - dist), falloff);
        }

        // ---- Public entry point ------------------------------------------

        /// <summary>Builds a fresh <see cref="TerrainData"/> for the biome using the given seed.</summary>
        public static TerrainData BuildTerrainData(BiomeAssetManifest biome, int seed)
        {
            int res = Mathf.Max(33, biome.heightmapResolution);
            var data = new TerrainData
            {
                heightmapResolution = res
            };
            data.size = biome.terrainSize;

            // Deterministic per-seed offset into noise space.
            var rng = new System.Random(seed);
            float seedOffset = (float)(rng.NextDouble() * 10000.0);

            int hmRes = data.heightmapResolution; // Unity may snap to 2^n+1
            float[,] heights = new float[hmRes, hmRes];
            for (int y = 0; y < hmRes; y++)
            {
                for (int x = 0; x < hmRes; x++)
                {
                    float baseH = SampleHeight(x, y, hmRes, biome.noiseSettings, seedOffset);
                    float mask = IslandMask(x, y, hmRes, biome.islandFalloff);
                    float h = biome.heightCurve.Evaluate(Mathf.Clamp01(baseH * mask));
                    heights[y, x] = Mathf.Clamp01(h);
                }
            }
            data.SetHeights(0, 0, heights);

            if (biome.terrainLayers != null && biome.terrainLayers.Length > 0)
            {
                data.terrainLayers = biome.terrainLayers;
                ApplyAlphamaps(data, biome);
            }

            return data;
        }

        // ---- Texturing ----------------------------------------------------

        static void ApplyAlphamaps(TerrainData data, BiomeAssetManifest biome)
        {
            int layers = biome.terrainLayers.Length;
            int aw = data.alphamapResolution;
            var maps = new float[aw, aw, layers];

            bool hasRules = biome.texturingRules != null && biome.texturingRules.Length > 0;

            for (int y = 0; y < aw; y++)
            {
                for (int x = 0; x < aw; x++)
                {
                    // Alphamap coords -> normalized terrain coords.
                    float nx = (float)x / (aw - 1);
                    float ny = (float)y / (aw - 1);

                    // GetSteepness / GetInterpolatedHeight take (u, v) in 0..1 where u maps to x, v to z.
                    float height01 = data.GetInterpolatedHeight(nx, ny) / Mathf.Max(0.0001f, data.size.y);
                    float slopeDeg = data.GetSteepness(nx, ny);

                    if (!hasRules)
                    {
                        // No rules authored: just lay down the first layer everywhere.
                        maps[y, x, 0] = 1f;
                        continue;
                    }

                    float total = 0f;
                    foreach (var rule in biome.texturingRules)
                    {
                        if (rule.layerIndex < 0 || rule.layerIndex >= layers) continue;
                        float hw = Band(height01, rule.minHeight, rule.maxHeight, Mathf.Max(0.0001f, rule.heightBlend));
                        float sw = Band(slopeDeg, rule.minSlope, rule.maxSlope, Mathf.Max(0.0001f, rule.slopeBlend));
                        float w = hw * sw * Mathf.Max(0f, rule.weight);
                        maps[y, x, rule.layerIndex] += w;
                        total += w;
                    }

                    if (total > 0.0001f)
                    {
                        for (int l = 0; l < layers; l++) maps[y, x, l] /= total;
                    }
                    else
                    {
                        // Nothing matched -> fall back to layer 0 so we never get a transparent splat.
                        maps[y, x, 0] = 1f;
                    }
                }
            }

            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>
        /// Smooth membership of <paramref name="v"/> in [min, max], with a soft fade of width
        /// <paramref name="blend"/> on each edge. Returns 0..1.
        /// </summary>
        static float Band(float v, float min, float max, float blend)
        {
            float rising = Mathf.SmoothStep(0f, 1f, (v - min) / blend);
            float falling = Mathf.SmoothStep(0f, 1f, (max - v) / blend);
            return Mathf.Clamp01(Mathf.Min(rising, falling));
        }

        // ---- Prop scatter -------------------------------------------------

        /// <summary>
        /// Scatters props from the biome onto the terrain as child GameObjects.
        /// Skips steep slopes and anything below <see cref="BiomeAssetManifest.propMinHeight"/>.
        /// </summary>
        public static void ScatterProps(Terrain terrain, BiomeAssetManifest biome, int seed)
        {
            if (biome.props == null || biome.props.Length == 0 || biome.propCount <= 0) return;

            var data = terrain.terrainData;
            var rng = new System.Random(seed * 31 + 7);
            Vector3 origin = terrain.transform.position;

            var holder = new GameObject("Props").transform;
            holder.SetParent(terrain.transform, false);

            int placed = 0, guard = biome.propCount * 8;
            while (placed < biome.propCount && guard-- > 0)
            {
                float u = (float)rng.NextDouble();
                float v = (float)rng.NextDouble();

                float height01 = data.GetInterpolatedHeight(u, v) / Mathf.Max(0.0001f, data.size.y);
                if (height01 < biome.propMinHeight) continue;
                if (data.GetSteepness(u, v) > biome.propMaxSlope) continue;

                var prefab = biome.props[rng.Next(biome.props.Length)];
                if (prefab == null) continue;

                Vector3 worldPos = origin + new Vector3(
                    u * data.size.x,
                    data.GetInterpolatedHeight(u, v),
                    v * data.size.z);

                GameObject go;
#if UNITY_EDITOR
                go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, holder);
                go.transform.position = worldPos;
#else
                go = Object.Instantiate(prefab, worldPos, Quaternion.identity, holder);
#endif
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                placed++;
            }
        }
    }
}
