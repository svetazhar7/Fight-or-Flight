using System.Collections.Generic;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Terrain construction for the island system. <see cref="BuildIslandFromType"/> (or
    /// <see cref="PopulateIslandFromType"/>) builds an island from an <see cref="IslandTypeDefinition"/>,
    /// whose weighted biomes are laid into elevation bands sized by their share of the island's land area.
    /// Stateless so the same input + different seeds give different islands.
    /// </summary>
    public static class IslandTerrainGenerator
    {
        /// <summary>An elevation slice [lo, hi] (normalized height) owned by one biome.</summary>
        public struct BiomeBand
        {
            public BiomeAssetManifest biome;
            public float lo;
            public float hi;
        }

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

        /// <summary>Builds the full masked + shaped heightmap (shared by both entry points).</summary>
        static float[,] BuildHeights(int hmRes, NoiseSettings noise, HeightProfileMode profile,
                                     int terraceSteps, AnimationCurve curve, float falloff, float seedOffset)
        {
            bool ridged = profile == HeightProfileMode.Ridged;
            var heights = new float[hmRes, hmRes];
            for (int y = 0; y < hmRes; y++)
            {
                for (int x = 0; x < hmRes; x++)
                {
                    float baseH = SampleHeight(x, y, hmRes, noise, seedOffset, ridged);
                    float mask = IslandMask(x, y, hmRes, falloff);
                    float h = curve.Evaluate(Mathf.Clamp01(baseH * mask));
                    if (profile == HeightProfileMode.Terraced)
                    {
                        int steps = Mathf.Max(2, terraceSteps);
                        h = Mathf.Round(h * steps) / steps;
                    }
                    heights[y, x] = Mathf.Clamp01(h);
                }
            }
            return heights;
        }

        // ---- Island from a type definition (multi-biome composition) -----

        /// <summary>
        /// Builds an island from an <see cref="IslandTypeDefinition"/>. Its weighted biomes are sorted by
        /// <see cref="BiomeAssetManifest.elevationOrder"/> and each is assigned an elevation band whose
        /// width is sized so the band's fraction of the island's LAND AREA equals the biome's percentage.
        /// The splatmap paints each band with the biome's real texture (or a solid <c>debugColor</c> layer
        /// while textures are missing). The resulting <paramref name="bands"/> drive per-biome spawning.
        /// </summary>
        public static TerrainData BuildIslandFromType(IslandTypeDefinition def, int seed,
            Vector3? sizeOverride, float waterlineNormalized, out List<BiomeBand> bands)
        {
            var data = new TerrainData();
            PopulateIslandFromType(data, def, seed, sizeOverride ?? def.terrainSize, waterlineNormalized, out bands);
            return data;
        }

        /// <summary>
        /// Fills an existing TerrainData with the composed island. When it will be saved as an asset, create
        /// the asset BEFORE calling this so the SetAlphamaps splatmap persists (CreateAsset drops in-memory
        /// splats — the cause of the "all beach" bug).
        /// </summary>
        public static void PopulateIslandFromType(TerrainData data, IslandTypeDefinition def, int seed,
            Vector3 size, float waterlineNormalized, out List<BiomeBand> bands)
        {
            bands = new List<BiomeBand>();

            data.heightmapResolution = Mathf.Max(33, def.heightmapResolution);
            data.size = size;
            data.alphamapResolution = 256;

            var rng = new System.Random(seed);
            float seedOffset = (float)(rng.NextDouble() * 10000.0);
            int hmRes = data.heightmapResolution;
            float[,] heights = BuildHeights(hmRes, def.noiseSettings, def.heightProfile,
                def.terraceSteps, def.heightCurve, def.islandFalloff, seedOffset);
            data.SetHeights(0, 0, heights);

            // Valid biomes, sorted low -> high.
            var entries = new List<WeightedBiome>();
            if (def.biomes != null)
                foreach (var w in def.biomes)
                    if (w != null && w.biome != null && w.percent > 0f) entries.Add(w);
            if (entries.Count == 0) return;
            entries.Sort((a, b) => a.biome.elevationOrder.CompareTo(b.biome.elevationOrder));

            int n = entries.Count;
            float totalPct = 0f;
            foreach (var e in entries) totalPct += e.percent;

            // Land-area percentile edges: edge[k+1] = height below which cumulative% of land lies.
            var land = new List<float>(hmRes * hmRes / 2);
            for (int y = 0; y < hmRes; y++)
                for (int x = 0; x < hmRes; x++)
                    if (heights[y, x] > waterlineNormalized) land.Add(heights[y, x]);
            if (land.Count == 0)
                for (int y = 0; y < hmRes; y++)
                    for (int x = 0; x < hmRes; x++) land.Add(heights[y, x]);
            land.Sort();

            float[] edges = new float[n + 1];
            edges[0] = 0f;
            edges[n] = 1f;
            float acc = 0f;
            for (int k = 0; k < n - 1; k++)
            {
                acc += entries[k].percent / totalPct;
                edges[k + 1] = Quantile(land, acc);
            }
            for (int k = 1; k <= n; k++)
                if (edges[k] < edges[k - 1]) edges[k] = edges[k - 1]; // keep monotonic

            // Terrain layers + band records.
            var layers = new TerrainLayer[n];
            for (int k = 0; k < n; k++)
            {
                layers[k] = GetBiomeLayer(entries[k].biome);
                bands.Add(new BiomeBand { biome = entries[k].biome, lo = edges[k], hi = edges[k + 1] });
            }
            data.terrainLayers = layers;

            // Paint splatmap by band with soft edges.
            int aw = data.alphamapResolution;
            var maps = new float[aw, aw, n];
            const float blend = 0.03f;
            for (int y = 0; y < aw; y++)
            {
                for (int x = 0; x < aw; x++)
                {
                    float nx = (float)x / (aw - 1);
                    float ny = (float)y / (aw - 1);
                    float h = data.GetInterpolatedHeight(nx, ny) / Mathf.Max(0.0001f, data.size.y);

                    // Cumulative band membership: weight_k = (fraction at/above edge[k]) - (… above edge[k+1]).
                    // This telescopes to a partition of unity, so cells sitting exactly on an edge split
                    // smoothly between neighbours instead of collapsing to zero (critical for terraced terrain).
                    float total = 0f;
                    for (int k = 0; k < n; k++)
                    {
                        float cumLow = (k == 0) ? 1f : AboveEdge(h, edges[k], blend);
                        float cumHigh = (k == n - 1) ? 0f : AboveEdge(h, edges[k + 1], blend);
                        float w = Mathf.Clamp01(cumLow - cumHigh);
                        maps[y, x, k] = w;
                        total += w;
                    }
                    if (total > 0.0001f) { for (int k = 0; k < n; k++) maps[y, x, k] /= total; }
                    else maps[y, x, 0] = 1f;
                }
            }
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>Smooth cumulative weight at/above edge <paramref name="e"/>: 0.5 at h==e, →1 above, →0 below.</summary>
        static float AboveEdge(float h, float e, float blend)
            => Mathf.SmoothStep(0f, 1f, (h - e) / (2f * blend) + 0.5f);

        static float Quantile(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return Mathf.Clamp01(p);
            int idx = Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[idx];
        }

        // ---- Texture layers ----------------------------------------------

        /// <summary>The biome's first real TerrainLayer, or a solid <c>debugColor</c> layer as a stand-in.</summary>
        static TerrainLayer GetBiomeLayer(BiomeAssetManifest biome)
        {
            if (biome.textureLayers != null)
                foreach (var l in biome.textureLayers)
                    if (l != null && l.terrainLayer != null) return l.terrainLayer;
            return GetOrCreateDebugLayer(biome);
        }

        /// <summary>A reusable solid-colour TerrainLayer used to preview a biome before art exists.</summary>
        static TerrainLayer GetOrCreateDebugLayer(BiomeAssetManifest biome)
        {
#if UNITY_EDITOR
            const string genFolder = "Assets/IslandSystem/Generated";
            const string dir = genFolder + "/Debug";
            if (!UnityEditor.AssetDatabase.IsValidFolder(genFolder))
                UnityEditor.AssetDatabase.CreateFolder("Assets/IslandSystem", "Generated");
            if (!UnityEditor.AssetDatabase.IsValidFolder(dir))
                UnityEditor.AssetDatabase.CreateFolder(genFolder, "Debug");

            string id = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(biome));
            if (string.IsNullOrEmpty(id)) id = biome.name;
            string texPath = dir + "/" + id + "_tex.asset";
            string layerPath = dir + "/" + id + ".terrainlayer";

            var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = new Texture2D(8, 8);
                UnityEditor.AssetDatabase.CreateAsset(tex, texPath);
            }
            FillColor(tex, biome.debugColor);
            UnityEditor.EditorUtility.SetDirty(tex);

            var layer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                UnityEditor.AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = tex;
            layer.tileSize = new Vector2(40f, 40f);
            UnityEditor.EditorUtility.SetDirty(layer);
            return layer;
#else
            var tex = new Texture2D(8, 8);
            FillColor(tex, biome.debugColor);
            return new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(40f, 40f) };
#endif
        }

        static void FillColor(Texture2D tex, Color c)
        {
            var px = new Color[tex.width * tex.height];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            tex.SetPixels(px);
            tex.Apply();
        }

        // ---- Condition helpers -------------------------------------------

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

        /// <summary>Scatters each composed biome's spawn rules, confined to that biome's elevation band.</summary>
        public static void ScatterCompositionObjects(Terrain terrain, IslandTypeDefinition def, int seed, List<BiomeBand> bands)
        {
            if (bands == null) return;
            int bi = 0;
            foreach (var band in bands)
            {
                bi++;
                if (band.biome == null || band.biome.spawnRules == null || band.biome.spawnRules.Count == 0) continue;
                ScatterRuleList(terrain, band.biome.spawnRules, seed + bi * 101, band.lo, band.hi);
            }
        }

        /// <summary>Places a list of spawn rules, gating each instance to the [bandLo, bandHi] height window.</summary>
        static void ScatterRuleList(Terrain terrain, List<ObjectSpawnRule> rules, int seed, float bandLo, float bandHi)
        {
            var data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;

            Transform holder = null;
            int ruleIndex = 0;
            foreach (var rule in rules)
            {
                ruleIndex++;
                if (rule == null || !rule.IsValid) continue;

                if (holder == null)
                {
                    holder = terrain.transform.Find("Spawned");
                    if (holder == null)
                    {
                        holder = new GameObject("Spawned").transform;
                        holder.SetParent(terrain.transform, false);
                    }
                }

                var rng = new System.Random(seed * 73856093 ^ ruleIndex * 19349663);
                int placed = 0, guard = rule.count * 12;
                while (placed < rule.count && guard-- > 0)
                {
                    float u = (float)rng.NextDouble();
                    float v = (float)rng.NextDouble();

                    float height01 = data.GetInterpolatedHeight(u, v) / Mathf.Max(0.0001f, data.size.y);
                    if (height01 < bandLo || height01 > bandHi) continue;
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
                    Quaternion rot = Quaternion.identity;
                    if (rule.alignToNormal)
                        rot = Quaternion.FromToRotation(Vector3.up, data.GetInterpolatedNormal(u, v));
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
