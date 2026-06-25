using System.Collections.Generic;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Pure terrain construction from a <see cref="BiomeAssetManifest"/>: heightmap (multi-octave Perlin,
    /// optionally ridged/terraced, masked into an island shape), splatmap (condition-driven texture
    /// layers) and object scatter (condition-driven spawn rules). Stateless on purpose so the same biome
    /// + different seeds give different islands.
    /// </summary>
    public static class IslandTerrainGenerator
    {
        // ---- Heightmap ----------------------------------------------------

        /// <summary>Normalized (0..1) multi-octave Perlin sample, optionally ridged.</summary>
        public static float SampleHeight(int x, int y, int size, NoiseSettings n, float seedOffset, bool ridged)
        {
            float h = 0f, amp = n.amplitude, freq = n.frequency, norm = 0f;
            int octaves = Mathf.Max(1, n.octaves);
            for (int o = 0; o < octaves; o++)
            {
                float v = Mathf.PerlinNoise(
                    (float)x / size * freq + seedOffset,
                    (float)y / size * freq + seedOffset);
                if (ridged) v = 1f - Mathf.Abs(v * 2f - 1f); // sharp ridgelines
                h += v * amp;
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

        /// <summary>
        /// Builds a fresh <see cref="TerrainData"/> for the biome using the given seed. Pass
        /// <paramref name="sizeOverride"/> to scale this island independently of the biome template
        /// (used by the archipelago to spawn islands of different sizes).
        /// </summary>
        public static TerrainData BuildTerrainData(BiomeAssetManifest biome, int seed, Vector3? sizeOverride = null)
        {
            int res = Mathf.Max(33, biome.heightmapResolution);
            var data = new TerrainData { heightmapResolution = res };
            data.size = sizeOverride ?? biome.terrainSize;

            var rng = new System.Random(seed);
            float seedOffset = (float)(rng.NextDouble() * 10000.0);
            bool ridged = biome.heightProfile == HeightProfileMode.Ridged;

            int hmRes = data.heightmapResolution; // Unity may snap to 2^n+1
            float[,] heights = new float[hmRes, hmRes];
            for (int y = 0; y < hmRes; y++)
            {
                for (int x = 0; x < hmRes; x++)
                {
                    float baseH = SampleHeight(x, y, hmRes, biome.noiseSettings, seedOffset, ridged);
                    float mask = IslandMask(x, y, hmRes, biome.islandFalloff);
                    float h = biome.heightCurve.Evaluate(Mathf.Clamp01(baseH * mask));
                    if (biome.heightProfile == HeightProfileMode.Terraced)
                    {
                        int steps = Mathf.Max(2, biome.terraceSteps);
                        h = Mathf.Round(h * steps) / steps;
                    }
                    heights[y, x] = Mathf.Clamp01(h);
                }
            }
            data.SetHeights(0, 0, heights);

            var layers = biome.CollectTerrainLayers();
            if (layers.Length > 0)
            {
                data.terrainLayers = layers;
                ApplyAlphamaps(data, biome, layers);
            }

            return data;
        }

        // ---- Texturing ----------------------------------------------------

        static void ApplyAlphamaps(TerrainData data, BiomeAssetManifest biome, TerrainLayer[] layers)
        {
            // Map each non-null BiomeTextureLayer to its index in the TerrainData layer array.
            var entries = new List<BiomeTextureLayer>();
            foreach (var l in biome.textureLayers)
                if (l != null && l.terrainLayer != null) entries.Add(l);

            int layerCount = layers.Length;
            int aw = data.alphamapResolution;
            var maps = new float[aw, aw, layerCount];

            for (int y = 0; y < aw; y++)
            {
                for (int x = 0; x < aw; x++)
                {
                    float nx = (float)x / (aw - 1);
                    float ny = (float)y / (aw - 1);
                    float height01 = data.GetInterpolatedHeight(nx, ny) / Mathf.Max(0.0001f, data.size.y);
                    float slopeDeg = data.GetSteepness(nx, ny);

                    float total = 0f;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        float w = ConditionWeight(entries[i].where, height01, slopeDeg) * Mathf.Max(0f, entries[i].weight);
                        maps[y, x, i] += w;
                        total += w;
                    }

                    if (total > 0.0001f)
                    {
                        for (int l = 0; l < layerCount; l++) maps[y, x, l] /= total;
                    }
                    else
                    {
                        maps[y, x, 0] = 1f; // never leave a transparent splat
                    }
                }
            }

            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>0..1 membership of a point (given its height &amp; slope) in a placement condition.</summary>
        public static float ConditionWeight(PlacementCondition c, float height01, float slopeDeg)
        {
            float hw = Band(height01, c.minHeight, c.maxHeight, Mathf.Max(0.0001f, c.heightBlend));
            float sw = Band(slopeDeg, c.minSlope, c.maxSlope, Mathf.Max(0.0001f, c.slopeBlend));
            return hw * sw;
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

        // ---- Object scatter ----------------------------------------------

        /// <summary>
        /// Scatters objects from the biome's spawn rules onto the terrain as child GameObjects.
        /// Each rule places its own count where its condition holds, with per-instance variation.
        /// </summary>
        public static void ScatterObjects(Terrain terrain, BiomeAssetManifest biome, int seed)
        {
            if (biome.spawnRules == null || biome.spawnRules.Count == 0) return;

            var data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;

            Transform holder = null;
            int ruleIndex = 0;
            foreach (var rule in biome.spawnRules)
            {
                ruleIndex++;
                if (rule == null || !rule.IsValid) continue;

                if (holder == null)
                {
                    holder = new GameObject("Spawned").transform;
                    holder.SetParent(terrain.transform, false);
                }

                var rng = new System.Random(seed * 73856093 ^ ruleIndex * 19349663);
                int placed = 0, guard = rule.count * 12;
                while (placed < rule.count && guard-- > 0)
                {
                    float u = (float)rng.NextDouble();
                    float v = (float)rng.NextDouble();

                    float height01 = data.GetInterpolatedHeight(u, v) / Mathf.Max(0.0001f, data.size.y);
                    float slopeDeg = data.GetSteepness(u, v);
                    if (ConditionWeight(rule.where, height01, slopeDeg) <= 0.001f) continue;

                    var prefab = rule.prefabs[rng.Next(rule.prefabs.Length)];
                    if (prefab == null) continue;

                    Vector3 worldPos = origin + new Vector3(
                        u * data.size.x,
                        data.GetInterpolatedHeight(u, v) - rule.sink,
                        v * data.size.z);

                    GameObject go;
#if UNITY_EDITOR
                    go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, holder);
                    go.transform.position = worldPos;
#else
                    go = Object.Instantiate(prefab, worldPos, Quaternion.identity, holder);
#endif
                    // Rotation: optional alignment to ground normal, then optional random yaw.
                    Quaternion rot = Quaternion.identity;
                    if (rule.alignToNormal)
                    {
                        Vector3 normal = data.GetInterpolatedNormal(u, v);
                        rot = Quaternion.FromToRotation(Vector3.up, normal);
                    }
                    if (rule.randomYRotation)
                        rot = rot * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    go.transform.rotation = rot;

                    float s = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                    if (s > 0f) go.transform.localScale = go.transform.localScale * s;

                    placed++;
                }
            }
        }
    }
}
