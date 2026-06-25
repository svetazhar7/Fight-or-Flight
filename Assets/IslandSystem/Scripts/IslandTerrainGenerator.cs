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

        /// <summary>A flattened build zone: ground inside is levelled to <see cref="smoothedHeight"/>.</summary>
        public struct VillageZone
        {
            public BiomeAssetManifest biome;
            public Vector2 centerUV;      // normalized tile coords of the centre
            public float radiusWorld;     // flat-zone radius (world units)
            public float smoothedHeight;  // local Y of the levelled ground (= normalized * size.y)
        }

        // ---- Heightmap ----------------------------------------------------

        /// <summary>Per-island randomized shape controls: coastline irregularity, elongation, placement.</summary>
        struct ShapeParams
        {
            public float seedOffset;
            public Vector2 center;        // small offset of the island within its tile
            public float rot;             // elongation orientation
            public float scaleX, scaleY;  // axis scales >= 1 (shrink only, so the island always fits the tile)
            public float coastWarp;       // strength of coastline distortion
        }

        static ShapeParams MakeShapeParams(System.Random rng)
        {
            float e = Mathf.Lerp(1f, 1.5f, (float)rng.NextDouble());
            bool stretchX = rng.NextDouble() < 0.5;
            return new ShapeParams
            {
                seedOffset = (float)(rng.NextDouble() * 10000.0),
                center = new Vector2((float)(rng.NextDouble() - 0.5) * 0.08f, (float)(rng.NextDouble() - 0.5) * 0.08f),
                rot = (float)(rng.NextDouble() * Mathf.PI),
                scaleX = stretchX ? e : 1f,
                scaleY = stretchX ? 1f : e,
                coastWarp = Mathf.Lerp(0.3f, 0.6f, (float)rng.NextDouble())
            };
        }

        /// <summary>Normalized (0..1) multi-octave Perlin sample at fractional coords, optionally ridged.</summary>
        public static float SampleHeightF(float fx, float fy, NoiseSettings n, float seedOffset, bool ridged)
        {
            float h = 0f, amp = n.amplitude, freq = n.frequency, norm = 0f;
            int octaves = Mathf.Max(1, n.octaves);
            for (int o = 0; o < octaves; o++)
            {
                float v = Mathf.PerlinNoise(fx * freq + seedOffset, fy * freq + seedOffset);
                if (ridged) v = 1f - Mathf.Abs(v * 2f - 1f); // sharp ridgelines
                h += v * amp;
                norm += amp;
                freq *= 2f;
                amp *= 0.5f;
            }
            return norm > 0f ? h / norm : h;
        }

        /// <summary>
        /// Irregular island mask: an offset / rotated / elongated blob with a noise-distorted coastline and a
        /// PLATEAU interior (≈1) that tapers to sea at the coast. The plateau is what lets peaks form wherever
        /// the terrain noise is high instead of always at the centre. Always 0 at the tile border (no cliffs).
        /// </summary>
        static float IslandShapeMask(int x, int y, int size, float falloff, ShapeParams sp)
        {
            float fx = (float)x / size, fy = (float)y / size;
            float u = fx - 0.5f - sp.center.x;
            float v = fy - 0.5f - sp.center.y;
            float cs = Mathf.Cos(sp.rot), sn = Mathf.Sin(sp.rot);
            float ru = (u * cs - v * sn) * sp.scaleX;
            float rv = (u * sn + v * cs) * sp.scaleY;
            float dist = Mathf.Sqrt(ru * ru + rv * rv) * 2.05f;

            // Distort the coastline so it isn't a clean circle (bays + peninsulas).
            float w1 = Mathf.PerlinNoise(fx * 3f + sp.seedOffset, fy * 3f + sp.seedOffset * 0.7f + 13.1f);
            float w2 = Mathf.PerlinNoise(fx * 6.5f + sp.seedOffset * 1.7f + 5.3f, fy * 6.5f + sp.seedOffset * 0.3f + 91.7f);
            dist += (w1 - 0.5f) * sp.coastWarp + (w2 - 0.5f) * sp.coastWarp * 0.45f;

            float edge = 1f - dist;
            if (edge <= 0f) return 0f;
            float coast = Mathf.Clamp(0.6f - falloff * 0.12f, 0.18f, 0.45f); // coast taper width
            float m = edge >= coast ? 1f : edge / coast;
            m = Mathf.SmoothStep(0f, 1f, m);

            // Rim guard: force sea at the very tile edge so land never makes a straight cliff there.
            float border = Mathf.Min(Mathf.Min(fx, 1f - fx), Mathf.Min(fy, 1f - fy));
            m *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(border / 0.04f));
            return m;
        }

        /// <summary>Builds the full masked + shaped heightmap with domain-warped, plateau-shaped terrain.</summary>
        static float[,] BuildHeights(int hmRes, NoiseSettings noise, HeightProfileMode profile,
                                     int terraceSteps, AnimationCurve curve, float falloff, ShapeParams sp)
        {
            bool ridged = profile == HeightProfileMode.Ridged;
            var heights = new float[hmRes, hmRes];
            const float warpAmp = 0.06f;
            for (int y = 0; y < hmRes; y++)
            {
                for (int x = 0; x < hmRes; x++)
                {
                    float fx = (float)x / hmRes, fy = (float)y / hmRes;
                    // Domain-warp the terrain noise for organic, non-radial features.
                    float wx = (Mathf.PerlinNoise(fx * 2f + sp.seedOffset + 31.1f, fy * 2f + sp.seedOffset + 17.7f) - 0.5f) * warpAmp;
                    float wy = (Mathf.PerlinNoise(fx * 2f + sp.seedOffset + 71.3f, fy * 2f + sp.seedOffset + 53.9f) - 0.5f) * warpAmp;
                    float baseH = SampleHeightF(fx + wx, fy + wy, noise, sp.seedOffset, ridged);
                    float mask = IslandShapeMask(x, y, hmRes, falloff, sp);
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
            PopulateIslandFromType(data, def, seed, sizeOverride ?? def.terrainSize, waterlineNormalized, out bands, out _);
            return data;
        }

        /// <summary>
        /// Fills an existing TerrainData with the composed island. When it will be saved as an asset, create
        /// the asset BEFORE calling this so the SetAlphamaps splatmap persists (CreateAsset drops in-memory
        /// splats — the cause of the "all beach" bug).
        /// </summary>
        public static void PopulateIslandFromType(TerrainData data, IslandTypeDefinition def, int seed,
            Vector3 size, float waterlineNormalized, out List<BiomeBand> bands, out List<VillageZone> villages,
            int resolutionOverride = 0)
        {
            bands = new List<BiomeBand>();
            villages = new List<VillageZone>();

            var rng = new System.Random(seed);
            ShapeParams shape = MakeShapeParams(rng);
            int hmRes = Mathf.Max(33, resolutionOverride > 0 ? resolutionOverride : def.heightmapResolution);
            float[,] heights = BuildHeights(hmRes, def.noiseSettings, def.heightProfile,
                def.terraceSteps, def.heightCurve, def.islandFalloff, shape);

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

            // Terrain layers + band records. (GetBiomeLayer may create debug-layer ASSETS, which can revert
            // the not-yet-saved TerrainData asset — so we configure `data` only AFTER this, at the very end.)
            var layers = new TerrainLayer[n];
            for (int k = 0; k < n; k++)
            {
                layers[k] = GetBiomeLayer(entries[k].biome);
                bands.Add(new BiomeBand { biome = entries[k].biome, lo = edges[k], hi = edges[k + 1] });
            }

            // ---- Villages: pick sites + FLATTEN the heightmap array now, before the splat & SetHeights so
            // the levelled ground is what gets textured and committed. ----
            ChooseAndFlattenVillages(heights, hmRes, size, bands, waterlineNormalized, seed, villages);

            // Paint splatmap by band with soft edges.
            const int aw = 256;
            var maps = new float[aw, aw, n];
            const float blend = 0.03f;
            for (int y = 0; y < aw; y++)
            {
                for (int x = 0; x < aw; x++)
                {
                    float nx = (float)x / (aw - 1);
                    float ny = (float)y / (aw - 1);
                    float h = SampleGrid(heights, nx, ny); // height (already normalized 0..1)

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
            // Configure the TerrainData and commit LAST — after all AssetDatabase writes above — so the
            // unsaved asset isn't reverted to its on-disk (default) resolution mid-build.
            data.heightmapResolution = hmRes;
            data.size = size;
            data.alphamapResolution = aw;
            data.terrainLayers = layers;
            data.SetHeights(0, 0, heights);
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>Bilinear sample of a normalized height grid (indexed [y, x]) at tile coords u,v in 0..1.</summary>
        static float SampleGrid(float[,] g, float u, float v)
        {
            int res = g.GetLength(0);
            float fx = Mathf.Clamp01(u) * (res - 1);
            float fy = Mathf.Clamp01(v) * (res - 1);
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Mathf.Min(x0 + 1, res - 1), y1 = Mathf.Min(y0 + 1, res - 1);
            float tx = fx - x0, ty = fy - y0;
            float a = Mathf.Lerp(g[y0, x0], g[y0, x1], tx);
            float b = Mathf.Lerp(g[y1, x0], g[y1, x1], tx);
            return Mathf.Lerp(a, b, ty);
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

        /// <summary>Normalized height below which a cell is the island's fade-to-sea zone (no objects there).</summary>
        const float FadeThreshold = 0.05f;

        /// <summary>Scatters each composed biome's generic prop rules (Props/) as GameObjects, per band, keeping out of villages.</summary>
        public static void ScatterCompositionObjects(Terrain terrain, IslandTypeDefinition def, int seed, List<BiomeBand> bands, List<VillageZone> villages)
        {
            if (bands == null) return;
            int bi = 0;
            foreach (var band in bands)
            {
                bi++;
                if (band.biome == null || band.biome.spawnRules == null || band.biome.spawnRules.Count == 0) continue;
                ScatterRuleList(terrain, band.biome.spawnRules, seed + bi * 101, band.lo, band.hi, "Spawned", villages);
            }
        }

        /// <summary>Scatters each composed biome's ROCK rules (Rocks/) as GameObjects under a "Rocks" holder, keeping out of villages.</summary>
        public static void ScatterRocks(Terrain terrain, IslandTypeDefinition def, int seed, List<BiomeBand> bands, List<VillageZone> villages)
        {
            if (bands == null) return;
            int bi = 0;
            foreach (var band in bands)
            {
                bi++;
                if (band.biome == null || band.biome.rockRules == null || band.biome.rockRules.Count == 0) continue;
                ScatterRuleList(terrain, band.biome.rockRules, seed + bi * 101, band.lo, band.hi, "Rocks", villages);
            }
        }

        /// <summary>
        /// Registers the biomes' tree prefabs (Trees/) as <see cref="TreePrototype"/>s and places them as
        /// Unity Terrain tree instances — each confined to its biome's elevation band, off the fade zone and
        /// off slopes the rule's condition rejects. Uses the island <paramref name="seed"/> for determinism.
        /// </summary>
        public static void PlaceTrees(Terrain terrain, IslandTypeDefinition def, int seed, List<BiomeBand> bands, List<VillageZone> villages)
        {
            var data = terrain.terrainData;
            if (bands == null) { data.treePrototypes = new TreePrototype[0]; data.SetTreeInstances(new TreeInstance[0], true); return; }

            // Collect unique tree prefabs across all biomes -> prototypes.
            var prototypes = new List<TreePrototype>();
            var protoIndex = new Dictionary<GameObject, int>();
            foreach (var band in bands)
            {
                if (band.biome == null || band.biome.treeRules == null) continue;
                foreach (var rule in band.biome.treeRules)
                {
                    if (rule == null || rule.prefabs == null) continue;
                    foreach (var p in rule.prefabs)
                        if (p != null && !protoIndex.ContainsKey(p))
                        {
                            protoIndex[p] = prototypes.Count;
                            prototypes.Add(new TreePrototype { prefab = p });
                        }
                }
            }
            data.treePrototypes = prototypes.ToArray();
            if (prototypes.Count == 0) { data.SetTreeInstances(new TreeInstance[0], true); return; }

            var instances = new List<TreeInstance>();
            int bi = 0;
            foreach (var band in bands)
            {
                bi++;
                if (band.biome == null || band.biome.treeRules == null) continue;
                int ruleIndex = 0;
                foreach (var rule in band.biome.treeRules)
                {
                    ruleIndex++;
                    if (rule == null || !rule.IsValid) continue;
                    var rng = new System.Random((seed + bi * 101) * 73856093 ^ ruleIndex * 19349663);
                    int placed = 0, guard = rule.count * 12;
                    while (placed < rule.count && guard-- > 0)
                    {
                        float u = (float)rng.NextDouble();
                        float v = (float)rng.NextDouble();
                        if (InAnyVillage(u, v, data.size, villages)) continue;
                        float height01 = data.GetInterpolatedHeight(u, v) / Mathf.Max(0.0001f, data.size.y);
                        if (height01 < FadeThreshold || height01 < band.lo || height01 > band.hi) continue;
                        float slopeDeg = data.GetSteepness(u, v);
                        if (ConditionWeight(rule.where, height01, slopeDeg) <= 0.001f) continue;

                        var prefab = rule.prefabs[rng.Next(rule.prefabs.Length)];
                        if (prefab == null || !protoIndex.TryGetValue(prefab, out int pi)) continue;

                        float s = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                        instances.Add(new TreeInstance
                        {
                            position = new Vector3(u, height01, v), // normalized; snapped to heightmap below
                            prototypeIndex = pi,
                            widthScale = s,
                            heightScale = s,
                            rotation = rule.randomYRotation ? (float)rng.NextDouble() * Mathf.PI * 2f : 0f,
                            color = Color.white,
                            lightmapColor = Color.white
                        });
                        placed++;
                    }
                }
            }
            data.SetTreeInstances(instances.ToArray(), true);
        }

        /// <summary>
        /// Places spawn rules as GameObjects under <paramref name="holderName"/>, gating each instance to the
        /// [bandLo, bandHi] height window and off the fade zone. Supports per-axis scale and normal alignment.
        /// </summary>
        static void ScatterRuleList(Terrain terrain, List<ObjectSpawnRule> rules, int seed, float bandLo, float bandHi, string holderName, List<VillageZone> villages)
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
                    holder = terrain.transform.Find(holderName);
                    if (holder == null)
                    {
                        holder = new GameObject(holderName).transform;
                        holder.SetParent(terrain.transform, false);
                    }
                }

                var rng = new System.Random(seed * 73856093 ^ ruleIndex * 19349663);
                int placed = 0, guard = rule.count * 12;
                while (placed < rule.count && guard-- > 0)
                {
                    float u = (float)rng.NextDouble();
                    float v = (float)rng.NextDouble();
                    if (InAnyVillage(u, v, data.size, villages)) continue;

                    float height01 = data.GetInterpolatedHeight(u, v) / Mathf.Max(0.0001f, data.size.y);
                    if (height01 < FadeThreshold || height01 < bandLo || height01 > bandHi) continue;
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

                    Vector3 baseScale = go.transform.localScale;
                    if (rule.nonUniformScale)
                    {
                        // Independent X/Y/Z scale so rocks look uneven.
                        float sx = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                        float sy = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                        float sz = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                        go.transform.localScale = Vector3.Scale(baseScale, new Vector3(sx, sy, sz));
                    }
                    else
                    {
                        float s = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                        if (s > 0f) go.transform.localScale = baseScale * s;
                    }

                    placed++;
                }
            }
        }

        // ---- Villages -----------------------------------------------------

        /// <summary>True if normalized point (u,v) lies inside any flattened village zone.</summary>
        static bool InAnyVillage(float u, float v, Vector3 size, List<VillageZone> villages)
        {
            if (villages == null) return false;
            float lx = u * size.x, lz = v * size.z;
            foreach (var z in villages)
            {
                float dx = lx - z.centerUV.x * size.x, dz = lz - z.centerUV.y * size.z;
                if (dx * dx + dz * dz < z.radiusWorld * z.radiusWorld) return true;
            }
            return false;
        }

        /// <summary>Slope (degrees) at normalized (u,v) from the height ARRAY (used before the terrain exists).</summary>
        static float SlopeAt(float[,] h, float u, float v, Vector3 size, int res)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 1, res - 2);
            int y = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 1, res - 2);
            float dhx = (h[y, x + 1] - h[y, x - 1]) * size.y / (2f * (size.x / (res - 1)));
            float dhz = (h[y + 1, x] - h[y - 1, x]) * size.y / (2f * (size.z / (res - 1)));
            return Mathf.Atan(Mathf.Sqrt(dhx * dhx + dhz * dhz)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Picks village sites for biomes with valid <see cref="VillageSettings"/> and FLATTENS the height
        /// array under each (level to the local average, SmoothStep blend on the rim). Prefers flat, in-band,
        /// off-fade ground far from other villages; if nothing qualifies it still uses the flattest candidate
        /// (the site is cleared of trees/rocks regardless).
        /// </summary>
        static void ChooseAndFlattenVillages(float[,] heights, int res, Vector3 size, List<BiomeBand> bands,
            float waterline, int seed, List<VillageZone> villages)
        {
            int bi = 0;
            foreach (var band in bands)
            {
                bi++;
                var biome = band.biome;
                if (biome == null || biome.village == null || !biome.village.IsValid) continue;
                var vs = biome.village;
                var rng = new System.Random(seed * 6151 + bi * 40503);

                // Candidates in this biome's band, above the fade zone, with their slope.
                var cands = new List<(float u, float v, float slope)>();
                int tries = Mathf.Max(400, vs.villageCount * 500);
                float fadeMin = Mathf.Max(waterline, FadeThreshold) + 0.03f;
                for (int t = 0; t < tries; t++)
                {
                    float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
                    float h = SampleGrid(heights, u, v);
                    if (h < fadeMin || h < band.lo || h > band.hi) continue;
                    cands.Add((u, v, SlopeAt(heights, u, v, size, res)));
                }
                cands.Sort((a, b) => a.slope.CompareTo(b.slope)); // flattest first (fallback-friendly)

                int placedForBiome = 0;
                foreach (var c in cands)
                {
                    if (placedForBiome >= vs.villageCount) break;
                    var center = new Vector2(c.u, c.v);
                    if (!FarFromVillages(center, size, villages, vs.minDistanceBetweenVillages)) continue;

                    float smoothedNorm = FlattenZone(heights, res, size, center, vs.villageRadius, vs.blendRadius);
                    villages.Add(new VillageZone
                    {
                        biome = biome,
                        centerUV = center,
                        radiusWorld = vs.villageRadius,
                        smoothedHeight = smoothedNorm * size.y
                    });
                    placedForBiome++;
                }
            }
        }

        static bool FarFromVillages(Vector2 centerUV, Vector3 size, List<VillageZone> villages, float minDist)
        {
            float lx = centerUV.x * size.x, lz = centerUV.y * size.z;
            foreach (var z in villages)
            {
                float dx = lx - z.centerUV.x * size.x, dz = lz - z.centerUV.y * size.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) < minDist) return false;
            }
            return true;
        }

        /// <summary>Levels the height array within the radius to its average, blending out over blendRadius. Returns the average (normalized).</summary>
        static float FlattenZone(float[,] heights, int res, Vector3 size, Vector2 centerUV, float radiusWorld, float blendWorld)
        {
            float cellX = size.x / (res - 1), cellZ = size.z / (res - 1);
            float cu = centerUV.x * (res - 1), cv = centerUV.y * (res - 1);
            float outer = radiusWorld + blendWorld;
            int rx = Mathf.CeilToInt(outer / cellX) + 1, rz = Mathf.CeilToInt(outer / cellZ) + 1;
            int x0 = Mathf.Clamp((int)cu - rx, 0, res - 1), x1 = Mathf.Clamp((int)cu + rx, 0, res - 1);
            int y0 = Mathf.Clamp((int)cv - rz, 0, res - 1), y1 = Mathf.Clamp((int)cv + rz, 0, res - 1);

            double sum = 0; int cnt = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x - cu) * cellX, dz = (y - cv) * cellZ;
                    if (dx * dx + dz * dz <= radiusWorld * radiusWorld) { sum += heights[y, x]; cnt++; }
                }
            if (cnt == 0) return SampleGrid(heights, centerUV.x, centerUV.y);
            float avg = (float)(sum / cnt);

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x - cu) * cellX, dz = (y - cv) * cellZ;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d <= radiusWorld) heights[y, x] = avg;
                    else if (d <= outer)
                    {
                        float t = Mathf.SmoothStep(0f, 1f, (d - radiusWorld) / Mathf.Max(0.0001f, blendWorld));
                        heights[y, x] = Mathf.Lerp(avg, heights[y, x], t);
                    }
                }
            return avg;
        }

        /// <summary>Places village buildings as GameObjects on the levelled ground of each zone (under a "Village" holder).</summary>
        public static void PlaceVillageBuildings(Terrain terrain, List<VillageZone> villages, int seed)
        {
            if (villages == null || villages.Count == 0) return;
            var data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;

            Transform holder = terrain.transform.Find("Village");
            if (holder == null) { holder = new GameObject("Village").transform; holder.SetParent(terrain.transform, false); }

            int zi = 0;
            foreach (var z in villages)
            {
                zi++;
                var vs = z.biome != null ? z.biome.village : null;
                if (vs == null || !vs.IsValid) continue;
                var rng = new System.Random(seed * 92821 + zi * 35317);

                float cx = z.centerUV.x * data.size.x, cz = z.centerUV.y * data.size.z;
                float placeR = z.radiusWorld * 0.85f; // keep buildings a touch inside the flat zone
                var placedXZ = new List<Vector2>();
                int placed = 0, guard = vs.buildingsPerVillage * 30;
                while (placed < vs.buildingsPerVillage && guard-- > 0)
                {
                    float r = placeR * Mathf.Sqrt((float)rng.NextDouble());
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    Vector2 xz = new Vector2(cx + Mathf.Cos(a) * r, cz + Mathf.Sin(a) * r);

                    bool ok = true;
                    foreach (var p in placedXZ)
                        if ((p - xz).sqrMagnitude < vs.minDistanceBetweenBuildings * vs.minDistanceBetweenBuildings) { ok = false; break; }
                    if (!ok) continue;

                    var prefab = vs.buildingPrefabs[rng.Next(vs.buildingPrefabs.Length)];
                    if (prefab == null) continue;

                    Vector3 worldPos = origin + new Vector3(xz.x, z.smoothedHeight, xz.y);
                    GameObject go;
#if UNITY_EDITOR
                    go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, holder);
                    go.transform.position = worldPos;
#else
                    go = Object.Instantiate(prefab, worldPos, Quaternion.identity, holder);
#endif
                    if (vs.randomYRotation) go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    float s = Mathf.Lerp(vs.buildingScaleRange.x, vs.buildingScaleRange.y, (float)rng.NextDouble());
                    if (s > 0f) go.transform.localScale = go.transform.localScale * s;

                    placedXZ.Add(xz);
                    placed++;
                }
            }
        }
    }
}
