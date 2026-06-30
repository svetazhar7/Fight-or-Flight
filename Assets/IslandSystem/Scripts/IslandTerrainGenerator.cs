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
            // Footprint scale: smaller land area than the tile so the island sits in open water with a margin,
            // but big enough to keep a substantial body + mountain (the gentle coast comes from the wide taper).
            float dist = Mathf.Sqrt(ru * ru + rv * rv) * 2.05f;

            // Distort the coastline so it isn't a clean circle (bays + peninsulas). Clamp the OUTWARD push so a
            // peninsula can't shoot back out to the tile edge and undo the margin (bays carve inward freely).
            float w1 = Mathf.PerlinNoise(fx * 3f + sp.seedOffset, fy * 3f + sp.seedOffset * 0.7f + 13.1f);
            float w2 = Mathf.PerlinNoise(fx * 6.5f + sp.seedOffset * 1.7f + 5.3f, fy * 6.5f + sp.seedOffset * 0.3f + 91.7f);
            float warp = (w1 - 0.5f) * sp.coastWarp + (w2 - 0.5f) * sp.coastWarp * 0.45f;
            dist += Mathf.Max(warp, -0.05f); // limit OUTWARD peninsulas so the coast never reaches the radial backstop

            float edge = 1f - dist;
            if (edge <= 0f) return 0f;
            // Wide coast taper: the outer island is a gentle slope from the shoreline up; the flat interior is
            // carved back in by the plains-flatten pass. Wide taper = long gradual shore, but not a flat pancake.
            float coast = Mathf.Clamp(1.0f - falloff * 0.08f, 0.55f, 0.82f);
            // CONCAVE beach profile (t^2): keeps the mask/height low and slowly-changing near the shoreline so the
            // land meets the water as a gentle slope (the steeper part is pushed up toward the centre).
            float t = Mathf.Clamp01(edge / coast);
            float m = t * t;

            // Far safety backstop only (sea by ~0.49 of the tile radius) — guarantees no tile-edge clipping; the
            // gentle coast itself comes from the wide taper above, not from this.
            float rad = Mathf.Sqrt((fx - 0.5f) * (fx - 0.5f) + (fy - 0.5f) * (fy - 0.5f));
            m *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.46f, 0.50f, rad));
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

        // ---- Biome-aware relief + Perlin erosion --------------------------

        /// <summary>
        /// Ridged multifractal (Musgrave) in 0..1 — the standard primitive for eroded mountains. Each octave's
        /// detail is gated by the previous octave's ridge strength (<c>weight</c>), so fine detail appears only
        /// on the ridges and the valley floors stay smooth: sharp peaks, eroded valleys, no derivatives needed.
        /// <paramref name="erosion"/> raises the gating gain → more aggressive valley smoothing.
        /// </summary>
        static float ErodedRidged(float fx, float fy, float baseFreq, int octaves, float seedOffset, float erosion)
        {
            float freq = baseFreq, amp = 1f, norm = 0f, result = 0f, weight = 1f;
            float gain = Mathf.Lerp(1.4f, 2.4f, Mathf.Clamp01(erosion));
            const float lacunarity = 2f, hExp = 0.9f;
            for (int o = 0; o < octaves; o++)
            {
                float n = Mathf.PerlinNoise(fx * freq + seedOffset, fy * freq + seedOffset);
                float signal = 1f - Mathf.Abs(2f * n - 1f); // ridge crest = 1, base = 0
                signal *= signal;                            // sharpen the ridgelines
                signal *= weight;                            // erosion: kill detail inside previous valleys
                weight = Mathf.Clamp01(signal * gain);
                result += signal * amp;
                norm += amp;
                amp *= Mathf.Pow(lacunarity, -hExp);         // spectral falloff
                freq *= lacunarity;
            }
            return norm > 0f ? Mathf.Clamp01(result / norm) : result;
        }

        /// <summary>
        /// Layers biome-driven relief on the macro <paramref name="heights"/> form (modified in place):
        /// low-relief biomes (beach/plain) are smoothed FLAT, high-relief biomes (hills/mountains) get sharp
        /// eroded ridge detail that grows taller with elevation. Each biome's ruggedness = its
        /// <see cref="BiomeAssetManifest.reliefStrength"/> biased by its <see cref="BiomeAssetManifest.elevationOrder"/>,
        /// so default-tuned biomes already read as "flatter low, rougher high".
        /// </summary>
        static void ApplyBiomeRelief(float[,] heights, int res, List<WeightedBiome> entries,
            float[] edges, IslandTypeDefinition def, ShapeParams shape, float waterline)
        {
            if (def.mountainRelief <= 0.0001f && def.plainsFlatten <= 0.0001f && def.microRoughness <= 0.0001f)
                return;

            // Per-band ruggedness keyed by the PERCENT-BASED height bands (the SAME edges the splat uses), so
            // changing a biome's % actually reshapes the terrain: more mountain % pushes the mountain band down
            // -> a larger, lower height range gets rugged & raised; more plain % -> more of the island is flat.
            // center[k] = mid height of biome k's band; rr[k] = its ruggedness (reliefStrength biased by where
            // the biome sits, so peak biomes stay rugged and shoreline biomes calm even at the 0.5 default).
            int m = entries.Count;
            var center = new float[m];
            var rr = new float[m];
            for (int k = 0; k < m; k++)
            {
                var b = entries[k].biome;
                center[k] = 0.5f * (edges[k] + edges[k + 1]);
                rr[k] = Mathf.Clamp01(b.reliefStrength * Mathf.Lerp(0.25f, 1.4f, Mathf.Clamp01(b.elevationOrder)));
            }

            float invLand = 1f / Mathf.Max(0.0001f, 1f - waterline);
            float seedOff = shape.seedOffset * 1.31f + 47.7f;

            // Pass 1: per-cell relief weight + the eroded mountain detail to add.
            var relief = new float[res, res];
            var add = new float[res, res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float form = heights[y, x];
                    if (form <= waterline) { relief[y, x] = 0f; add[y, x] = 0f; continue; }

                    float e = Mathf.Clamp01((form - waterline) * invLand);   // 0 shore .. 1 peak (height factor)
                    float r = SampleReliefCurve(center, rr, form);           // ruggedness of the band at this height
                    relief[y, x] = r;

                    float fx = (float)x / res, fy = (float)y / res;
                    float d = ErodedRidged(fx, fy, def.reliefFrequency, def.reliefOctaves, seedOff, def.erosionStrength);
                    // Ridges (d~1) raise the land, shallow valleys (d~0) carve a little below the macro form.
                    float detail = d - 0.22f;
                    // Taller with absolute elevation; scaled by this biome's ruggedness and the master amount.
                    add[y, x] = def.mountainRelief * r * detail * (0.4f + 0.6f * e);
                }
            }

            // Pass 2: flatten low-relief ground. The plains' rolling comes from the LOW-frequency macro form, so
            // a small blur can't kill it — instead pull each low-relief cell toward a LARGE-radius blur (the
            // local elevation trend). At full strength a plain becomes locally flat (it still follows the
            // island's gross slope) — "minimal hills" plains; rugged ground (high relief) is left alone.
            if (def.plainsFlatten > 0.0001f)
            {
                int radius = Mathf.Max(6, Mathf.RoundToInt(res * 0.14f));
                var trend = BoxBlur(heights, res, radius);
                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float hgt = heights[y, x];
                        // Fade the flatten out at the coast so it doesn't drag the sea-skirt / shoreline UP toward
                        // the inland trend (that re-created height at the tile edge => clipping). Plains only.
                        float coast = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(waterline, waterline + 0.05f, hgt));
                        float w = def.plainsFlatten * (1f - relief[y, x]) * coast;
                        heights[y, x] = Mathf.Lerp(hgt, trend[y, x], w);
                    }
                }
            }

            // Pass 3: add the eroded mountain detail + a touch of micro-roughness everywhere on land.
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float hh = heights[y, x];
                    if (hh > waterline && def.microRoughness > 0.0001f)
                    {
                        float fx = (float)x / res, fy = (float)y / res;
                        float micro = Mathf.PerlinNoise(fx * def.reliefFrequency * 3.7f + seedOff + 5.1f,
                                                        fy * def.reliefFrequency * 3.7f + seedOff + 9.3f);
                        hh += def.microRoughness * (micro - 0.5f);
                    }
                    heights[y, x] = Mathf.Clamp01(hh + add[y, x]);
                }
            }
        }

        /// <summary>
        /// Separable box blur with a sliding-window running sum, so cost is O(res²) regardless of
        /// <paramref name="radius"/> — used to extract the broad elevation trend for flattening the plains.
        /// </summary>
        static float[,] BoxBlur(float[,] src, int res, int radius)
        {
            if (radius < 1) return (float[,])src.Clone();
            var tmp = new float[res, res];
            for (int y = 0; y < res; y++)                       // horizontal pass
            {
                float sum = 0f; int cnt = 0;
                for (int x = 0; x <= radius && x < res; x++) { sum += src[y, x]; cnt++; }
                for (int x = 0; x < res; x++)
                {
                    tmp[y, x] = sum / cnt;
                    int addIdx = x + radius + 1, remIdx = x - radius;
                    if (addIdx < res) { sum += src[y, addIdx]; cnt++; }
                    if (remIdx >= 0) { sum -= src[y, remIdx]; cnt--; }
                }
            }
            var dst = new float[res, res];
            for (int x = 0; x < res; x++)                       // vertical pass
            {
                float sum = 0f; int cnt = 0;
                for (int y = 0; y <= radius && y < res; y++) { sum += tmp[y, x]; cnt++; }
                for (int y = 0; y < res; y++)
                {
                    dst[y, x] = sum / cnt;
                    int addIdx = y + radius + 1, remIdx = y - radius;
                    if (addIdx < res) { sum += tmp[addIdx, x]; cnt++; }
                    if (remIdx >= 0) { sum -= tmp[remIdx, x]; cnt--; }
                }
            }
            return dst;
        }

        /// <summary>Piecewise-linear sample of the (band-centre height -> ruggedness) control points at x in 0..1.</summary>
        static float SampleReliefCurve(float[] xs, float[] rr, float x)
        {
            int m = xs.Length;
            if (m == 0) return 0f;
            if (x <= xs[0]) return rr[0];
            if (x >= xs[m - 1]) return rr[m - 1];
            for (int k = 1; k < m; k++)
            {
                if (x <= xs[k])
                {
                    float span = Mathf.Max(1e-5f, xs[k] - xs[k - 1]);
                    float t = (x - xs[k - 1]) / span;
                    return Mathf.Lerp(rr[k - 1], rr[k], t);
                }
            }
            return rr[m - 1];
        }

        /// <summary>
        /// Land-area percentile band edges: <c>edges[k+1]</c> is the height below which the cumulative % of the
        /// biomes' weights lies, so each biome owns a height band sized to its share of the island's land area.
        /// Driven by the biome PERCENTAGES — this is what makes both the relief and the splat respond to them.
        /// </summary>
        static float[] ComputeBandEdges(float[,] heights, int res, List<WeightedBiome> entries, float waterline)
        {
            int n = entries.Count;
            float totalPct = 0f;
            foreach (var e in entries) totalPct += e.percent;
            if (totalPct <= 0f) totalPct = 1f;

            var land = new List<float>(res * res / 2);
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    if (heights[y, x] > waterline) land.Add(heights[y, x]);
            if (land.Count == 0)
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++) land.Add(heights[y, x]);
            land.Sort();

            var edges = new float[n + 1];
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
            return edges;
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

            // Valid biomes, sorted low -> high. Computed BEFORE the heightmap so the relief pass can shape
            // each elevation band by its biome's character (plains flat, mountains rugged).
            var entries = new List<WeightedBiome>();
            if (def.biomes != null)
                foreach (var w in def.biomes)
                    if (w != null && w.biome != null && w.percent > 0f) entries.Add(w);
            if (entries.Count == 0) return;
            entries.Sort((a, b) => a.biome.elevationOrder.CompareTo(b.biome.elevationOrder));

            // Macro island form first. Then the relief pass needs the PERCENT-BASED band edges so a biome's %
            // drives its ruggedness — compute them from the macro form, shape the relief, then RE-compute the
            // edges from the final heights so the splat/bands line up with the terrain the relief produced.
            float[,] heights = BuildHeights(hmRes, def.noiseSettings, def.heightProfile,
                def.terraceSteps, def.heightCurve, def.islandFalloff, shape);
            float[] macroEdges = ComputeBandEdges(heights, hmRes, entries, waterlineNormalized);
            ApplyBiomeRelief(heights, hmRes, entries, macroEdges, def, shape, waterlineNormalized);

            int n = entries.Count;
            float[] edges = ComputeBandEdges(heights, hmRes, entries, waterlineNormalized);

            // Terrain layers — a biome can blend SEVERAL textures by weight (%). Build a flat, de-duplicated
            // layer list, and for each biome remember which channels it uses + their weights/conditions so the
            // biome's band can be split across them. (GetOrCreateDebugLayer may create ASSETS that revert the
            // unsaved TerrainData, so `data` is configured LAST.)
            var allLayers = new List<TerrainLayer>();
            var layerIndex = new Dictionary<TerrainLayer, int>();
            var subChan = new List<int>[n];
            var subW = new List<float>[n];
            var subWhere = new List<PlacementCondition>[n];
            for (int k = 0; k < n; k++)
            {
                var biome = entries[k].biome;
                subChan[k] = new List<int>(); subW[k] = new List<float>(); subWhere[k] = new List<PlacementCondition>();

                var valid = new List<BiomeTextureLayer>();
                if (biome.textureLayers != null)
                    foreach (var l in biome.textureLayers)
                        if (l != null && l.terrainLayer != null) valid.Add(l);

                if (valid.Count == 0)
                {
                    // No real textures yet: one solid debug-colour layer for the whole biome.
                    allLayers.Add(GetOrCreateDebugLayer(biome));
                    subChan[k].Add(allLayers.Count - 1); subW[k].Add(1f); subWhere[k].Add(PlacementCondition.Everywhere);
                }
                else
                {
                    float sumRaw = 0f;
                    foreach (var l in valid) sumRaw += Mathf.Max(0f, l.weight);
                    bool equal = sumRaw <= 0.0001f; // all weights left at 0 => blend the textures evenly
                    foreach (var l in valid)
                    {
                        if (!layerIndex.TryGetValue(l.terrainLayer, out int ch))
                        {
                            ch = allLayers.Count; allLayers.Add(l.terrainLayer); layerIndex[l.terrainLayer] = ch;
                        }
                        subChan[k].Add(ch);
                        subW[k].Add(equal ? 1f : Mathf.Max(0f, l.weight));
                        subWhere[k].Add(l.where);
                    }
                }
                bands.Add(new BiomeBand { biome = biome, lo = edges[k], hi = edges[k + 1] });
            }
            int layerCount = allLayers.Count;

            // ---- Villages: pick sites + FLATTEN the heightmap array now, before the splat & SetHeights so
            // the levelled ground is what gets textured and committed. ----
            ChooseAndFlattenVillages(heights, hmRes, size, bands, waterlineNormalized, seed, villages);

            int maxSub = 1;
            for (int k = 0; k < n; k++) maxSub = Mathf.Max(maxSub, subChan[k].Count);
            var eff = new float[maxSub];
            float patchSeed = shape.seedOffset * 0.91f + 123.4f;

            // Paint splatmap: biome band membership, then within each biome carve PATCHES of its textures from a
            // Perlin field — each texture covers ~its weight's share of the biome AREA, but in spatially-coherent
            // regions (e.g. snow showing up "местами" on the mountains), not a uniform mixed-grey blend. A
            // texture's `where` (height/slope) further restricts WHERE its patches can appear.
            const int aw = 256;
            var maps = new float[aw, aw, layerCount];
            const float blend = 0.03f;
            for (int y = 0; y < aw; y++)
            {
                for (int x = 0; x < aw; x++)
                {
                    float nx = (float)x / (aw - 1);
                    float ny = (float)y / (aw - 1);
                    float h = SampleGrid(heights, nx, ny);     // height (already normalized 0..1)
                    float slope = SlopeAt(heights, nx, ny, size, hmRes);

                    // Cumulative band membership: weight_k = (fraction at/above edge[k]) - (… above edge[k+1]).
                    // Telescopes to a partition of unity, so cells on an edge split smoothly between neighbours.
                    float total = 0f;
                    for (int k = 0; k < n; k++)
                    {
                        float cumLow = (k == 0) ? 1f : AboveEdge(h, edges[k], blend);
                        float cumHigh = (k == n - 1) ? 0f : AboveEdge(h, edges[k + 1], blend);
                        float wBiome = Mathf.Clamp01(cumLow - cumHigh);
                        if (wBiome <= 0f) continue;
                        total += wBiome;

                        var chans = subChan[k]; var ws = subW[k]; var wheres = subWhere[k];
                        int sc = chans.Count;
                        if (sc == 1) { maps[y, x, chans[0]] += wBiome; continue; }

                        // Area share each texture wants here (weight × where condition).
                        float sumEff = 0f;
                        for (int j = 0; j < sc; j++) { eff[j] = ws[j] * TextureConditionWeight(wheres[j], h, slope); sumEff += eff[j]; }
                        if (sumEff <= 0.0001f) { maps[y, x, chans[0]] += wBiome; continue; }

                        // Carve patches: partition the [0,1] noise value into per-texture segments sized by share.
                        float pfreq = entries[k].biome.texturePatchScale > 0.01f ? entries[k].biome.texturePatchScale : PatchFrequency;
                        float nval = PatchNoise(nx, ny, pfreq, patchSeed + k * 37.7f);
                        float acc = 0f;
                        for (int j = 0; j < sc; j++)
                        {
                            float lo = acc / sumEff; acc += eff[j]; float hi = acc / sumEff;
                            float memb = (j == 0 ? 1f : AboveEdge(nval, lo, PatchBlend))
                                       - (j == sc - 1 ? 0f : AboveEdge(nval, hi, PatchBlend));
                            if (memb > 0f) maps[y, x, chans[j]] += wBiome * Mathf.Clamp01(memb);
                        }
                    }
                    if (total > 0.0001f) { float inv = 1f / total; for (int c = 0; c < layerCount; c++) maps[y, x, c] *= inv; }
                    else maps[y, x, 0] = 1f;
                }
            }
            // Configure the TerrainData and commit LAST — after all AssetDatabase writes above — so the
            // unsaved asset isn't reverted to its on-disk (default) resolution mid-build.
            data.heightmapResolution = hmRes;
            data.size = size;
            data.alphamapResolution = aw;
            data.terrainLayers = allLayers.ToArray();
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

        /// <summary>Base spatial frequency of the texture-patch field (≈ how many patches across the tile).</summary>
        const float PatchFrequency = 6f;
        /// <summary>Soft-edge width of a texture patch, in noise-value units (bigger = softer patch borders).</summary>
        const float PatchBlend = 0.04f;

        /// <summary>2-octave Perlin in 0..1 used to lay out texture patches within a biome.</summary>
        static float PatchNoise(float fx, float fy, float freq, float seedOffset)
        {
            float a = Mathf.PerlinNoise(fx * freq + seedOffset, fy * freq + seedOffset * 0.7f + 5.3f);
            float b = Mathf.PerlinNoise(fx * freq * 2.3f + seedOffset * 1.7f + 11.1f, fy * freq * 2.3f + seedOffset * 0.3f + 19.7f);
            float v = a * 0.7f + b * 0.3f;
            // Mathf.PerlinNoise clusters around ~0.5; stretch its typical range to fill 0..1 so the per-texture
            // noise segments (sized by weight) actually map to ~their share of the area.
            return Mathf.Clamp01(Mathf.InverseLerp(0.30f, 0.70f, v));
        }

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
            if (Application.isPlaying)
            {
                // Runtime (incl. editor play mode): in-memory layer, no AssetDatabase.
                var rtex = new Texture2D(8, 8);
                FillColor(rtex, biome.debugColor);
                return new TerrainLayer { diffuseTexture = rtex, tileSize = new Vector2(40f, 40f) };
            }
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

        /// <summary>
        /// 0..1 membership of a point (given its height &amp; slope) in a placement condition. An empty/unset
        /// range (max &lt;= min, e.g. a default-zeroed condition) means "no restriction on that axis" — otherwise
        /// a degenerate condition rejects everything (the bug that stopped trees/objects spawning).
        /// </summary>
        public static float ConditionWeight(PlacementCondition c, float height01, float slopeDeg)
        {
            float hw = (c.maxHeight <= c.minHeight + 1e-4f) ? 1f : Band(height01, c.minHeight, c.maxHeight, Mathf.Max(0.0001f, c.heightBlend));
            float sw = (c.maxSlope <= c.minSlope + 1e-4f) ? 1f : Band(slopeDeg, c.minSlope, c.maxSlope, Mathf.Max(0.0001f, c.slopeBlend));
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

        /// <summary>
        /// Condition weight for TEXTURE blending. Like <see cref="ConditionWeight"/> but the soft fade is
        /// skipped at the domain boundaries, so a full-range "Everywhere" condition returns 1 everywhere (a
        /// plain percentage blend), while a restricted height/slope band still masks the texture.
        /// </summary>
        static float TextureConditionWeight(PlacementCondition c, float height01, float slopeDeg)
        {
            // An empty/unset range (max <= min, e.g. a default-zeroed condition) means "no restriction" — the
            // layer participates everywhere in the biome and the blend is driven purely by its weight (%).
            float hw = (c.maxHeight <= c.minHeight + 1e-4f)
                ? 1f : BandOpen(height01, c.minHeight, c.maxHeight, Mathf.Max(0.0001f, c.heightBlend), 0f, 1f);
            float sw = (c.maxSlope <= c.minSlope + 1e-4f)
                ? 1f : BandOpen(slopeDeg, c.minSlope, c.maxSlope, Mathf.Max(0.0001f, c.slopeBlend), 0f, 90f);
            return hw * sw;
        }

        /// <summary>As <see cref="Band"/>, but no fade where an edge sits on the domain limit (full range = 1).</summary>
        static float BandOpen(float v, float min, float max, float blend, float domainMin, float domainMax)
        {
            float rising = (min <= domainMin + 1e-4f) ? 1f : Mathf.SmoothStep(0f, 1f, (v - min) / blend);
            float falling = (max >= domainMax - 1e-4f) ? 1f : Mathf.SmoothStep(0f, 1f, (max - v) / blend);
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

            // Collect unique tree prefabs across all biomes -> prototypes (+ each one's horizontal footprint
            // radius, used to keep trees from spawning inside one another).
            var prototypes = new List<TreePrototype>();
            var protoIndex = new Dictionary<GameObject, int>();
            var protoRadius = new List<float>();
            float maxScale = 0.0001f;
            foreach (var band in bands)
            {
                if (band.biome == null || band.biome.treeRules == null) continue;
                foreach (var rule in band.biome.treeRules)
                {
                    if (rule == null || rule.prefabs == null) continue;
                    maxScale = Mathf.Max(maxScale, rule.scaleRange.x, rule.scaleRange.y);
                    foreach (var p in rule.prefabs)
                        if (p != null && !protoIndex.ContainsKey(p))
                        {
                            protoIndex[p] = prototypes.Count;
                            prototypes.Add(new TreePrototype { prefab = p });
                            protoRadius.Add(PrototypeRadius(p));
                        }
                }
            }
            data.treePrototypes = prototypes.ToArray();
            if (prototypes.Count == 0) { data.SetTreeInstances(new TreeInstance[0], true); return; }

            // Global no-overlap acceleration grid: a tree is rejected if it lands within (rA+rB)*pack of an
            // already-placed tree, ACROSS all rules/species — so nothing spawns inside another tree.
            float maxR = 0.2f; foreach (var r in protoRadius) maxR = Mathf.Max(maxR, r);
            float cell = Mathf.Max(1f, maxR * maxScale * 2f);
            const float pack = 0.8f;
            var occ = new Dictionary<long, List<Vector3>>();    // cell -> list of (worldX, worldZ, radius)
            long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            bool TooClose(float wx, float wz, float r)
            {
                int cx = Mathf.FloorToInt(wx / cell), cz = Mathf.FloorToInt(wz / cell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (occ.TryGetValue(Key(cx + dx, cz + dz), out var list))
                            foreach (var t in list)
                            {
                                float md = (r + t.z) * pack;
                                float ex = t.x - wx, ez = t.y - wz;
                                if (ex * ex + ez * ez < md * md) return true;
                            }
                return false;
            }
            void Register(float wx, float wz, float r)
            {
                long k = Key(Mathf.FloorToInt(wx / cell), Mathf.FloorToInt(wz / cell));
                if (!occ.TryGetValue(k, out var list)) { list = new List<Vector3>(); occ[k] = list; }
                list.Add(new Vector3(wx, wz, r));
            }

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
                    if (rule == null || rule.prefabs == null || rule.prefabs.Length == 0) continue;

                    // DENSITY-based placement: trees per 100 m² -> a spacing of ≈10/sqrt(density) metres. A jittered
                    // grid at that spacing gives roughly that density; each candidate is gated to the biome band,
                    // off villages/fade, by the rule's condition, AND by the global no-overlap check.
                    float density = rule.density > 0f ? rule.density : 2f;
                    float spacing = 10f / Mathf.Sqrt(density);
                    int gx = Mathf.Max(1, Mathf.CeilToInt(data.size.x / spacing));
                    int gz = Mathf.Max(1, Mathf.CeilToInt(data.size.z / spacing));
                    var rng = new System.Random((seed + bi * 101) * 73856093 ^ ruleIndex * 19349663);
                    const float jitter = 0.8f;

                    for (int iz = 0; iz < gz; iz++)
                    {
                        for (int ix = 0; ix < gx; ix++)
                        {
                            float u = Mathf.Clamp01((ix + 0.5f + ((float)rng.NextDouble() - 0.5f) * jitter) / gx);
                            float v = Mathf.Clamp01((iz + 0.5f + ((float)rng.NextDouble() - 0.5f) * jitter) / gz);
                            if (InAnyVillage(u, v, data.size, villages)) continue;
                            float height01 = data.GetInterpolatedHeight(u, v) / Mathf.Max(0.0001f, data.size.y);
                            if (height01 < FadeThreshold || height01 < band.lo || height01 > band.hi) continue;
                            float slopeDeg = data.GetSteepness(u, v);
                            // Soft edge: thin out toward the condition's limits instead of a hard cut.
                            if ((float)rng.NextDouble() > ConditionWeight(rule.where, height01, slopeDeg)) continue;

                            // Pick the species by weight (the mix ratio), then size + footprint.
                            int idx = PickWeighted(rule.prefabWeights, rule.prefabs.Length, rng);
                            var prefab = rule.prefabs[idx];
                            if (prefab == null || !protoIndex.TryGetValue(prefab, out int pi)) continue;
                            float s = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());

                            float wx = u * data.size.x, wz = v * data.size.z;
                            float radius = protoRadius[pi] * s;
                            if (TooClose(wx, wz, radius)) continue; // would spawn inside another tree
                            Register(wx, wz, radius);

                            instances.Add(new TreeInstance
                            {
                                position = new Vector3(u, height01, v), // normalized; snapped to heightmap by Unity
                                prototypeIndex = pi,
                                widthScale = s,
                                heightScale = s,
                                rotation = rule.randomYRotation ? (float)rng.NextDouble() * Mathf.PI * 2f : 0f,
                                color = Color.white,
                                lightmapColor = Color.white
                            });
                        }
                    }
                }
            }
            data.SetTreeInstances(instances.ToArray(), true);
        }

        /// <summary>Approx horizontal footprint radius (world units) of a prefab from its mesh bounds × scale.</summary>
        static float PrototypeRadius(GameObject prefab)
        {
            if (prefab == null) return 0.5f;
            bool any = false; Bounds b = default;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var e = mf.sharedMesh.bounds.extents; var s = mf.transform.lossyScale;
                var wb = new Bounds(Vector3.zero, new Vector3(Mathf.Abs(e.x * s.x), Mathf.Abs(e.y * s.y), Mathf.Abs(e.z * s.z)) * 2f);
                if (!any) { b = wb; any = true; } else b.Encapsulate(wb);
            }
            return any ? Mathf.Max(0.2f, Mathf.Max(b.extents.x, b.extents.z)) : 0.5f;
        }

        /// <summary>Weighted random index into a pool of <paramref name="count"/> items (the species mix ratio).</summary>
        static int PickWeighted(float[] weights, int count, System.Random rng)
        {
            if (count <= 1) return 0;
            if (weights == null || weights.Length == 0) return rng.Next(count);
            float total = 0f;
            for (int i = 0; i < count; i++) total += (i < weights.Length ? Mathf.Max(0f, weights[i]) : 0f);
            if (total <= 0.0001f) return rng.Next(count); // all-zero weights -> equal mix
            float r = (float)rng.NextDouble() * total, acc = 0f;
            for (int i = 0; i < count; i++)
            {
                acc += (i < weights.Length ? Mathf.Max(0f, weights[i]) : 0f);
                if (r <= acc) return i;
            }
            return count - 1;
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
        /// Picks village sites for biomes with valid <see cref="VillageSettings"/> and SMOOTHS the height
        /// array under each (gentle blur that keeps broad elevation but removes sharp bumps). Prefers flat,
        /// in-band, off-fade ground far from other villages, and requires the whole village disk to sit
        /// inland (off the coast/tile edge) so it can't be clipped.
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
                    // Keep the whole village (radius + a coastal buffer) on land and off the tile border so
                    // the settlement never overhangs the shoreline and gets clipped.
                    if (!VillageFitsInland(heights, center, size, vs.villageRadius + vs.blendRadius, waterline, 0.06f)) continue;

                    float smoothedNorm = SmoothZone(heights, res, size, center, vs.villageRadius, vs.blendRadius);
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

        /// <summary>
        /// Smooths the height array within the zone (iterative 3×3 blur) so the ground KEEPS its broad
        /// height variation but loses sharp local bumps — buildings sit on gently rolling, not flat,
        /// terrain. Smoothing strength fades to 0 over <paramref name="blendWorld"/> so it blends into the
        /// surrounding relief. Returns the resulting centre height (normalized).
        /// </summary>
        static float SmoothZone(float[,] heights, int res, Vector3 size, Vector2 centerUV, float radiusWorld, float blendWorld)
        {
            float cellX = size.x / (res - 1), cellZ = size.z / (res - 1);
            float cu = centerUV.x * (res - 1), cv = centerUV.y * (res - 1);
            float outer = radiusWorld + blendWorld;
            int rx = Mathf.CeilToInt(outer / cellX) + 2, rz = Mathf.CeilToInt(outer / cellZ) + 2;
            int x0 = Mathf.Clamp((int)cu - rx, 1, res - 2), x1 = Mathf.Clamp((int)cu + rx, 1, res - 2);
            int y0 = Mathf.Clamp((int)cv - rz, 1, res - 2), y1 = Mathf.Clamp((int)cv + rz, 1, res - 2);
            if (x1 < x0 || y1 < y0) return SampleGrid(heights, centerUV.x, centerUV.y);

            // More passes on bigger zones → consistently gentle slopes regardless of island size.
            int iterations = Mathf.Clamp(Mathf.RoundToInt(radiusWorld / Mathf.Max(cellX, cellZ) / 2f), 5, 18);
            for (int it = 0; it < iterations; it++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x - cu) * cellX, dz = (y - cv) * cellZ;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d > outer) continue;
                        float w = d <= radiusWorld ? 1f
                            : 1f - Mathf.SmoothStep(0f, 1f, (d - radiusWorld) / Mathf.Max(0.0001f, blendWorld));
                        float avg = (heights[y, x]
                            + heights[y, x - 1] + heights[y, x + 1]
                            + heights[y - 1, x] + heights[y + 1, x]
                            + heights[y - 1, x - 1] + heights[y - 1, x + 1]
                            + heights[y + 1, x - 1] + heights[y + 1, x + 1]) / 9f;
                        heights[y, x] = Mathf.Lerp(heights[y, x], avg, w);
                    }
            return SampleGrid(heights, centerUV.x, centerUV.y);
        }

        /// <summary>
        /// True if the whole village disk (<paramref name="radiusWorld"/>) sits on land above
        /// <paramref name="waterline"/> and inside the tile by <paramref name="edgeMargin"/> (UV) — so the
        /// settlement never overhangs the coast and gets clipped at the island edge.
        /// </summary>
        static bool VillageFitsInland(float[,] heights, Vector2 centerUV, Vector3 size, float radiusWorld, float waterline, float edgeMargin)
        {
            const int spokes = 20;
            for (int ring = 1; ring <= 2; ring++)
            {
                float rr = radiusWorld * (ring == 1 ? 0.6f : 1f);
                for (int s = 0; s < spokes; s++)
                {
                    float a = Mathf.PI * 2f * s / spokes;
                    float u = centerUV.x + Mathf.Cos(a) * rr / size.x;
                    float v = centerUV.y + Mathf.Sin(a) * rr / size.z;
                    if (u < edgeMargin || u > 1f - edgeMargin || v < edgeMargin || v > 1f - edgeMargin) return false;
                    if (SampleGrid(heights, u, v) <= waterline) return false;
                }
            }
            return true;
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

                    float bu = Mathf.Clamp01(xz.x / data.size.x), bv = Mathf.Clamp01(xz.y / data.size.z);
                    if (data.GetSteepness(bu, bv) > 24f) continue; // skip cliffs; gentler ground gets a flat pad
                    float padHNorm = data.GetInterpolatedHeight(bu, bv) / Mathf.Max(0.0001f, data.size.y);
                    Vector3 worldPos = origin + new Vector3(xz.x, padHNorm * data.size.y, xz.y);

                    GameObject go;
#if UNITY_EDITOR
                    go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, holder);
#else
                    go = Object.Instantiate(prefab, holder);
#endif
                    go.transform.position = worldPos;
                    if (vs.randomYRotation) go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    float s = Mathf.Lerp(vs.buildingScaleRange.x, vs.buildingScaleRange.y, (float)rng.NextDouble());
                    if (s > 0f) go.transform.localScale = go.transform.localScale * s;

                    // Level a footprint-sized pad under THIS building to its base height, then drop the model
                    // so its lowest point rests on the pad — each house sits flush (no float, no sinking),
                    // while the village keeps varied elevation between houses.
                    float foot = FootprintRadius(go);
                    FlattenPad(data, bu, bv, padHNorm, foot + 1.0f, foot * 0.7f);
                    float bottom = RendererBottomY(go);
                    go.transform.position += new Vector3(0f, worldPos.y - bottom - 0.1f, 0f);

                    placedXZ.Add(xz);
                    placed++;
                }
            }
        }

        /// <summary>Horizontal half-extent (world units) of a prefab instance's combined renderer bounds.</summary>
        static float FootprintRadius(GameObject go)
        {
            bool has = false; Bounds b = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
            return has ? Mathf.Max(Mathf.Max(b.extents.x, b.extents.z), 1.5f) : 4f;
        }

        /// <summary>Lowest world-space Y of a prefab instance's renderers (its base), for snapping to the ground.</summary>
        static float RendererBottomY(GameObject go)
        {
            bool has = false; float minY = go.transform.position.y;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            { float m = r.bounds.min.y; if (!has) { minY = m; has = true; } else minY = Mathf.Min(minY, m); }
            return minY;
        }

        /// <summary>
        /// Levels a flat pad in the LIVE terrain heightmap: cells within <paramref name="padRadius"/> are set
        /// to <paramref name="padHNorm"/>, blending back to the existing relief over <paramref name="padBlend"/>.
        /// Used per-building so each house gets level ground without flattening the whole village.
        /// </summary>
        static void FlattenPad(TerrainData data, float u, float v, float padHNorm, float padRadius, float padBlend)
        {
            int res = data.heightmapResolution;
            Vector3 size = data.size;
            float cellX = size.x / (res - 1), cellZ = size.z / (res - 1);
            float cu = u * (res - 1), cv = v * (res - 1);
            float outer = padRadius + padBlend;
            int rx = Mathf.CeilToInt(outer / cellX) + 1, rz = Mathf.CeilToInt(outer / cellZ) + 1;
            int x0 = Mathf.Clamp((int)cu - rx, 0, res - 1), x1 = Mathf.Clamp((int)cu + rx, 0, res - 1);
            int y0 = Mathf.Clamp((int)cv - rz, 0, res - 1), y1 = Mathf.Clamp((int)cv + rz, 0, res - 1);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            if (w <= 0 || h <= 0) return;

            float[,] hh = data.GetHeights(x0, y0, w, h);
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    float dx = (x0 + xx - cu) * cellX, dz = (y0 + yy - cv) * cellZ;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d <= padRadius) hh[yy, xx] = padHNorm;
                    else if (d <= outer)
                    {
                        float t = Mathf.SmoothStep(0f, 1f, (d - padRadius) / Mathf.Max(0.0001f, padBlend));
                        hh[yy, xx] = Mathf.Lerp(padHNorm, hh[yy, xx], t);
                    }
                }
            data.SetHeights(x0, y0, hh);
        }
    }
}
