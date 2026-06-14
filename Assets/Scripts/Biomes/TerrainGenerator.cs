using System.Collections.Generic;
using UnityEngine;

[System.Serializable] public class NoiseSettings { [Tooltip("How many noise features span the whole map. Lower = larger, smoother regions.")] public float featureScale = 3f; [Range(1, 8)] public int octaves = 4; [Range(0f, 1f)] public float persistence = 0.5f; public float lacunarity = 2f; public Vector2 offset; }

/// <summary>Shape of the playable landmass, defined by where the border mountains ring it.</summary>
public enum TerrainShape { Square, Rectangle, Circle }

public class TerrainGenerator : MonoBehaviour
{
    [Header("Map Shape")]
    [Tooltip("Shape of the playable area inside the border mountains: Square (default), Rectangle (wide), or Circle (round island/sea ringed by mountains; corners become mountain).")]
    public TerrainShape terrainShape = TerrainShape.Square;

    [Tooltip("Rectangle only: width-to-height ratio of the playable area (>1 = wider than tall).")]
    [Range(1f, 3f)] public float rectangleAspect = 1.7f;

    [Tooltip("Circle/Rectangle: width of the coastline band (normalized map units) over which land slopes down into the surrounding ocean.")]
    [Range(0.01f, 0.2f)] public float coastWidth = 0.06f;

    [Tooltip("Circle/Rectangle: how far below the waterline the carved sea floor outside the land sits (normalized height). Bigger = deeper offshore water.")]
    [Range(0.01f, 0.3f)] public float seaFloorDrop = 0.06f;

    [Tooltip("Circle/Rectangle: how much the interior is lifted into dry land so the shape reads as a solid island (0 = no lift, just a footprint; higher = pronounced landmass above the sea).")]
    [Range(0f, 0.6f)] public float islandUplift = 0.28f;

    [Tooltip("Circle/Rectangle: distance inland (normalized) over which the interior rises from the coast to its full island height.")]
    [Range(0.02f, 0.4f)] public float islandCoreWidth = 0.16f;

    [Header("Map Size")][Tooltip("Physical map size in kilometers (square). Bigger map = larger, smoother biomes.")] public float mapSizeKm = 20f;

    [Tooltip("Maximum terrain height in meters.")]
    public float terrainHeight = 2000f;

    [Tooltip("Height curve exponent for the visual terrain. >1 flattens lowlands into broad plains and keeps mountains as rare, large landforms (more realistic). Does not affect biome classification.")]
    [Range(1f, 5f)] public float heightRedistribution = 3.2f;

    [Tooltip("Heightmap resolution. Snapped to a valid value: 513, 1025, 2049 or 4097. Higher = more detail but slower.")]
    public int heightmapResolution = 2049;

    [Tooltip("Resolution of the generated biome color map texture.")]
    public int colormapResolution = 1024;

    [Tooltip("Octaves used when classifying biomes by height. Low = large, smooth biome regions (terrain detail is unaffected).")]
    [Range(1, 5)] public int biomeHeightOctaves = 2;

    [Header("Biome Regions (Minecraft-style chunks)")]
    [Tooltip("How many distinct climate biomes appear on the whole map. The map is split into a few big regions, each one of these biomes.")]
    [Range(1, 8)] public int biomesPerMap = 3;

    [Tooltip("Region cells per axis. Fewer = bigger chunks. (3 -> up to 9 cells, merged by biome into a few large regions.)")]
    [Range(2, 12)] public int regionCellsPerAxis = 4;

    [Tooltip("How much the region borders meander (0 = straight Voronoi edges, high = very organic).")]
    [Range(0f, 0.5f)] public float regionWarp = 0.18f;

    [Tooltip("Width of the soft blend band between regions (in normalized map units). Higher = biomes bleed further into each other.")]
    [Range(0.005f, 0.5f)] public float regionBorderBlend = 0.14f;

    [Header("Border Mountains")]
    [Tooltip("Raise the map edges into tall mountains that mark the world boundary.")]
    public bool borderMountains = true;

    [Tooltip("Width of the border mountain belt as a fraction of the map (scales with map size).")]
    [Range(0.01f, 0.45f)] public float borderWidthPercent = 0.08f;

    [Tooltip("Target height of the border ridge (1 = max terrain height, taller than normal mountains).")]
    [Range(0.5f, 1f)] public float borderPeakHeight = 1f;

    [Header("Height Noise")]
    public NoiseSettings heightNoise = new NoiseSettings { featureScale = 3f, octaves = 4, persistence = 0.42f, lacunarity = 2f };

    [Header("Temperature Noise")]
    public NoiseSettings temperatureNoise = new NoiseSettings { featureScale = 1.5f, octaves = 2, persistence = 0.5f, lacunarity = 2f, offset = new Vector2(1000f, 1000f) };

    [Header("Humidity Noise")]
    public NoiseSettings humidityNoise = new NoiseSettings { featureScale = 2f, octaves = 3, persistence = 0.5f, lacunarity = 2f, offset = new Vector2(2000f, 500f) };

    [Header("Terrain Ruggedness (height-dependent)")]
    [Tooltip("Fine detail noise added on top of the base shape. Its strength scales with elevation so mountains get rugged while plains stay smooth.")]
    public NoiseSettings detailNoise = new NoiseSettings { featureScale = 28f, octaves = 4, persistence = 0.5f, lacunarity = 2.2f };

    [Tooltip("Detail amplitude on the lowlands (kept small so plains read as flat).")]
    [Range(0f, 0.08f)] public float lowlandRoughness = 0.003f;

    [Tooltip("Detail amplitude on the highlands (large so mountains are jagged and relief-heavy).")]
    [Range(0f, 0.3f)] public float highlandRoughness = 0.14f;

    [Tooltip("How quickly ruggedness ramps up with elevation. >1 keeps roughness concentrated on the higher peaks, so lowlands stay flat far up the slope.")]
    [Range(0.5f, 5f)] public float ruggednessCurve = 2.8f;

    [Tooltip("Tiny high-frequency relief applied everywhere so no surface is perfectly glassy-flat.")]
    [Range(0f, 0.01f)] public float microRoughness = 0.0012f;

    [Header("Surface Look (matte / earthy)")]
    [Tooltip("Strength of the per-pixel earthy grain baked into the terrain color (breaks up the flat, glossy look).")]
    [Range(0f, 0.6f)] public float surfaceGrain = 0.22f;

    [Tooltip("Frequency of the surface grain in colormap pixels. Higher = finer speckle.")]
    [Range(0.02f, 1f)] public float grainScale = 0.25f;

    [Header("Water (ocean)")]
    public TerrainWaterBuilder.Settings water = new TerrainWaterBuilder.Settings
    {
        // Ocean: flat sea plane at sea level; the depth-based toon water shader
        // floods every lowland below it.
        oceansEnabled = true,
        seaLevel = 0.12f,

        waterShallow = new Color(0.30f, 0.55f, 0.62f),
        waterDeep = new Color(0.10f, 0.32f, 0.50f)
    };

    [Tooltip("Subdivisions per axis of the flat ocean plane (kept low; it's a flat sheet driven by the depth shader).")]
    [Range(1, 200)] public int oceanSubdivisions = 64;

    [Tooltip("How irregular the border mountains are (0 = straight wall, 1 = very meandering/natural).")]
    [Range(0f, 1f)] public float borderRoughness = 0.6f;

    private Vector2 _borderOffset;
    private Vector2 _grainOffset;

    public float TerrainSizeMeters => Mathf.Max(1f, mapSizeKm) * 1000f;

    /// <summary>
    /// Generates the world as pure data (heightmap + colormap + biome map) with
    /// no Unity Terrain objects. The chunk streaming system builds the visual
    /// terrain from windows of this data on each client independently.
    /// Deterministic from the seed, so all networked peers produce identical data.
    /// </summary>
    public WorldData GenerateWorldData(int seed, BiomeData[] biomes, float biomeBlend, int biomeMapResolution, out BiomeMap biomeMap)
    {
        float sizeMeters = TerrainSizeMeters;
        int res = SnapHeightmapResolution(heightmapResolution);

        // Seeded, repeatable per-channel offsets.
        System.Random rng = new System.Random(seed);
        Vector2 heightOff = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);
        Vector2 tempOff = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);
        Vector2 humOff = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);
        Vector2 detailOff = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);
        _borderOffset = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);
        _grainOffset = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);

        // Build the large biome regions. Each region's biome is decided by the
        // climate (temperature + humidity) at its centre, so the original
        // classification idea is preserved - just at region granularity.
        var climateBiomes = new List<BiomeData>();
        if (biomes != null)
            foreach (var b in biomes)
                if (b != null && b.placement == BiomePlacement.Climate)
                    climateBiomes.Add(b);

        System.Func<float, float, float> sampleTemp = (nx, nz) => SampleNoise(nx, nz, temperatureNoise, tempOff);
        System.Func<float, float, float> sampleHum = (nx, nz) => SampleNoise(nx, nz, humidityNoise, humOff);

        var regions = new BiomeRegions(climateBiomes, biomesPerMap, regionCellsPerAxis,
            regionWarp, regionBorderBlend, new System.Random(seed * 31 + 7), sampleTemp, sampleHum);

        biomeMap = new BiomeMap(biomeMapResolution, biomes, biomeBlend, regions);

        // 1) Build the heightmap at full resolution with height-dependent ruggedness.
        float[,] heights = new float[res, res];
        for (int z = 0; z < res; z++)
        {
            float nz = (float)z / (res - 1);
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);

                // Power curve: broad flat lowlands, mountains stay as rare large landforms.
                float baseH = Mathf.Pow(SampleNoise(nx, nz, heightNoise, heightOff), heightRedistribution);
                baseH = ApplyShape(baseH, nx, nz);

                heights[z, x] = Mathf.Clamp01(ApplyRuggedness(baseH, nx, nz, detailOff));
            }
        }

        // 1b) Ocean: fill isolated below-sea-level pockets up above the waterline
        //     so the flat sea plane only shows the main connected ocean(s) - no
        //     stray water sitting in little basins up on the mountainsides.
        if (water.oceansEnabled)
            FillIsolatedBasins(heights, res, water.seaLevel);

        // 2) Build the biome map at its own (low) resolution.
        int bres = biomeMap.Resolution;

        // Pass 1: raw classification height per cell, smoothed (low-octave) so
        // regions stay large but shaped like the real terrain (pow + border).
        float[] rawH = new float[bres * bres];
        for (int z = 0; z < bres; z++)
        {
            float nz = (float)z / (bres - 1);
            for (int x = 0; x < bres; x++)
            {
                float nx = (float)x / (bres - 1);
                float h = Mathf.Pow(SampleNoise(nx, nz, heightNoise, heightOff, biomeHeightOctaves), heightRedistribution);
                rawH[z * bres + x] = ApplyShape(h, nx, nz);
            }
        }

        // Convert heights to terrain-relative percentiles so elevation biome
        // thresholds read as intuitive fractions (e.g. snow = top 10% of land),
        // regardless of how the power curve squashes the absolute height values.
        float[] sortedH = (float[])rawH.Clone();
        System.Array.Sort(sortedH);

        // Pass 2: classify + flag water.
        for (int z = 0; z < bres; z++)
        {
            float nz = (float)z / (bres - 1);
            for (int x = 0; x < bres; x++)
            {
                float nx = (float)x / (bres - 1);
                float hp = Percentile(sortedH, rawH[z * bres + x]);
                float t = sampleTemp(nx, nz);
                float hum = sampleHum(nx, nz);
                biomeMap.SetPoint(x, z, nx, nz, hp, t, hum);

                // The Ocean biome IS the water: any cell whose actual terrain is
                // below the waterline becomes Ocean (blue, flagged water), so the
                // biome map and the water mesh always describe the same sea.
                if (water.oceansEnabled)
                {
                    int hx = Mathf.Clamp(Mathf.RoundToInt(nx * (res - 1)), 0, res - 1);
                    int hz = Mathf.Clamp(Mathf.RoundToInt(nz * (res - 1)), 0, res - 1);
                    if (heights[hz, hx] < water.seaLevel)
                        biomeMap.SetOcean(x, z);
                }
            }
        }

        // 3) Bake the biome colormap texture (shared by all chunks and the far mesh).
        Texture2D colorTex = BuildColormap(biomeMap);

        return new WorldData
        {
            seed = seed,
            heights = heights,
            heightRes = res,
            worldSize = new Vector3(sizeMeters, terrainHeight, sizeMeters),
            colormap = colorTex,
            biomeMap = biomeMap,
        };
    }

    // Adds detail noise whose amplitude grows with elevation: smooth plains,
    // jagged mountains. A tiny micro-relief is added everywhere so the surface
    // is never perfectly flat/glossy.
    private float ApplyRuggedness(float baseH, float nx, float nz, Vector2 detailOff)
    {
        float dval = SampleNoise(nx, nz, detailNoise, detailOff);     // 0..1
        float smoothDetail = dval - 0.5f;                              // gentle bumps
        float ridged = (1f - Mathf.Abs(dval * 2f - 1f)) - 0.5f;       // sharp ridges for peaks

        float highT = Mathf.Pow(Mathf.Clamp01(baseH), ruggednessCurve);
        float detail = Mathf.Lerp(smoothDetail, ridged, highT);
        float amp = Mathf.Lerp(lowlandRoughness, highlandRoughness, highT);

        float micro = (Mathf.PerlinNoise(nx * 220f + detailOff.y, nz * 220f + detailOff.x) - 0.5f) * microRoughness;

        return baseH + detail * amp + micro;
    }

    /// <summary>
    /// Bakes the biome colors (plus earthy grain) into one global texture.
    /// Streamed chunks each show their own window of it via terrain layer tile
    /// offsets; the far horizon mesh uses it directly. Underwater terrain is left
    /// untinted - the ocean's depth-based toon water shader colours it.
    /// </summary>
    private Texture2D BuildColormap(BiomeMap biomeMap)
    {
        int res = Mathf.Clamp(Mathf.ClosestPowerOfTwo(colormapResolution), 256, 2048);
        Texture2D colorTex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        colorTex.filterMode = FilterMode.Bilinear;
        colorTex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[res * res];
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                int bx = Mathf.Clamp(Mathf.RoundToInt((float)x / (res - 1) * (biomeMap.Resolution - 1)), 0, biomeMap.Resolution - 1);
                int bz = Mathf.Clamp(Mathf.RoundToInt((float)z / (res - 1) * (biomeMap.Resolution - 1)), 0, biomeMap.Resolution - 1);
                Color c = biomeMap.GetColor(bx, bz);

                // Earthy grain: blotchy per-pixel brightness variation.
                float g = (Mathf.PerlinNoise(x * grainScale + _grainOffset.x, z * grainScale + _grainOffset.y) - 0.5f)
                          + (Mathf.PerlinNoise(x * grainScale * 3.3f + _grainOffset.y, z * grainScale * 3.3f + _grainOffset.x) - 0.5f) * 0.5f;
                float factor = Mathf.Clamp(1f + g * surfaceGrain, 0.6f, 1.35f);
                c.r *= factor; c.g *= factor; c.b *= factor;

                // URP Terrain Lit reads smoothness from the base map's alpha. Keep it
                // near 0 so the ground is matte/earthy instead of wet-looking plastic.
                c.a = 0f;
                pixels[z * res + x] = c;
            }
        }

        colorTex.SetPixels(pixels);
        colorTex.Apply();
        return colorTex;
    }

    // Distance (0..0.5) from a point to the edge of the playable area for the
    // selected map shape. 0 at the boundary, grows inward; negative outside the
    // shape (e.g. the corners of a Circle) so those become full mountain wall.
    private float ShapeInset(float nx, float nz)
    {
        switch (terrainShape)
        {
            case TerrainShape.Circle:
            {
                // Round island/sea: inset = how far inside the inscribed circle.
                float dx = nx - 0.5f, dz = nz - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dz * dz); // 0 centre .. ~0.707 corners
                return 0.5f - r;                         // 0.5 centre, 0 at radius .5, <0 in corners
            }
            case TerrainShape.Rectangle:
            {
                // Squeeze one axis so the flat area is wider than it is tall.
                float a = Mathf.Max(1f, rectangleAspect);
                float ex = Mathf.Min(nx, 1f - nx);
                float ez = Mathf.Min(nz, 1f - nz) * a;
                return Mathf.Min(ex, ez);
            }
            default: // Square
                return Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(nz, 1f - nz));
        }
    }

    // Shapes the world boundary according to terrainShape:
    //  - Square: optional border-mountain ring (the classic walled, bounded map).
    //  - Circle / Rectangle: the land is an actual disc/rectangle and everything
    //    outside it sinks below the waterline into open ocean - a real shaped
    //    island, no invisible wall.
    private float ApplyShape(float h, float nx, float nz)
    {
        switch (terrainShape)
        {
            case TerrainShape.Circle:
            case TerrainShape.Rectangle:
                return ApplyOceanCoast(h, nx, nz);
            default:
                return ApplyBorderRidge(h, nx, nz);
        }
    }

    // Outside the shape's footprint the terrain drops to a sea floor below the
    // waterline, so the land really is shaped (round/rectangular) and is ringed
    // by ocean instead of a mountain wall. A coastline band gives a natural beach.
    private float ApplyOceanCoast(float h, float nx, float nz)
    {
        float inset = ShapeInset(nx, nz);           // >0 inside footprint, <0 outside
        float coast = Mathf.Max(0.005f, coastWidth);

        // Lift the interior into dry land so the footprint reads as an island:
        // 0 at the coast, ramping to the full island height further inland.
        float landT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coast, coast + islandCoreWidth, inset));
        float landH = Mathf.Clamp01(h + islandUplift * landT);

        if (inset >= coast) return landH;           // solidly inland

        // Coastline band: blend the (lifted) land down to the offshore sea floor.
        float seaFloor = Mathf.Max(0f, water.seaLevel - seaFloorDrop);
        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coast, -coast, inset));
        return Mathf.Lerp(landH, seaFloor, t);
    }

    // Raises height toward borderPeakHeight near the map edges, forming a tall
    // boundary ridge. edgeDist is 0 at the boundary and grows inward (shape-aware).
    private float ApplyBorderRidge(float h, float nx, float nz)
    {
        if (!borderMountains) return h;

        float edgeDist = ShapeInset(nx, nz);
        float width = Mathf.Clamp(borderWidthPercent, 0.001f, 0.5f);

        // Meander the band width along the rim so the inner edge is irregular
        // instead of a perfectly straight line.
        float rim = Mathf.PerlinNoise(nx * 8f + _borderOffset.x, nz * 8f + _borderOffset.y); // 0..1
        float varAmount = Mathf.Clamp01(borderRoughness);
        float widthVar = width * Mathf.Lerp(1f, Mathf.Lerp(0.5f, 1.6f, rim), varAmount);

        float u = Mathf.Clamp01(edgeDist / widthVar);
        // Smootherstep for a softer, more natural shoulder than smoothstep.
        float s = u * u * u * (u * (u * 6f - 15f) + 10f);
        float t = 1f - s; // 1 at edge -> 0 inland

        // Vary the ridge crest so it has high summits and lower saddles.
        float peakNoise = Mathf.PerlinNoise(nx * 14f + _borderOffset.y, nz * 14f + _borderOffset.x);
        float peak = borderPeakHeight * Mathf.Lerp(1f, Mathf.Lerp(0.72f, 1f, peakNoise), varAmount);

        // Fine roughness concentrated on the slope so it isn't glassy-smooth.
        float rough = (Mathf.PerlinNoise(nx * 40f + _borderOffset.x, nz * 40f + _borderOffset.y) - 0.5f)
                      * 0.07f * varAmount * t;

        float raised = Mathf.Lerp(h, peak, t) + rough;
        return Mathf.Clamp01(raised);
    }

    [Header("Ocean cleanup")]
    [Tooltip("Below-sea water regions smaller than this fraction of the map are treated as stray pockets and filled in (raised above sea level) so only real, large oceans/seas show water. 0 = keep every pocket.")]
    [Range(0f, 0.05f)] public float oceanMinRegionFraction = 0.004f;

    [Tooltip("Resolution of the connectivity scan that decides what counts as a real ocean vs a stray pocket. Coarse is fine and fast.")]
    [Range(64, 513)] public int oceanScanResolution = 257;

    // Flood-fills the below-sea-level cells, keeps only connected regions large
    // enough to be real oceans/seas, and raises every other (isolated) pocket
    // just above the waterline. The flat ocean plane then renders water solely
    // over the big connected bodies - no lakes perched in mountain basins.
    private void FillIsolatedBasins(float[,] heights, int res, float seaLevel)
    {
        int gr = Mathf.Clamp(oceanScanResolution, 64, res);
        var below = new bool[gr, gr];
        for (int z = 0; z < gr; z++)
            for (int x = 0; x < gr; x++)
            {
                int hz = z * (res - 1) / (gr - 1);
                int hx = x * (res - 1) / (gr - 1);
                below[z, x] = heights[hz, hx] < seaLevel;
            }

        // Label 4-connected components and measure their sizes.
        var label = new int[gr, gr];
        var sizes = new List<int>();
        var stack = new Stack<int>();
        int comp = 0;
        for (int z0 = 0; z0 < gr; z0++)
            for (int x0 = 0; x0 < gr; x0++)
            {
                if (!below[z0, x0] || label[z0, x0] != 0) continue;
                comp++;
                int size = 0;
                stack.Push(z0 * gr + x0);
                label[z0, x0] = comp;
                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    int cz = idx / gr, cx = idx % gr;
                    size++;
                    PushIf(below, label, stack, gr, cz + 1, cx, comp);
                    PushIf(below, label, stack, gr, cz - 1, cx, comp);
                    PushIf(below, label, stack, gr, cz, cx + 1, comp);
                    PushIf(below, label, stack, gr, cz, cx - 1, comp);
                }
                sizes.Add(size);
            }

        if (comp == 0) return;
        int minCells = Mathf.Max(1, Mathf.RoundToInt(oceanMinRegionFraction * gr * gr));

        // Raise every full-res below-sea cell whose region is too small to keep.
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                if (heights[z, x] >= seaLevel) continue;
                int cz = z * (gr - 1) / (res - 1);
                int cx = x * (gr - 1) / (res - 1);
                int c = label[cz, cx];
                if (c == 0 || sizes[c - 1] < minCells)
                    heights[z, x] = seaLevel + 0.0015f; // tuck just above the waterline
            }
    }

    private static void PushIf(bool[,] below, int[,] label, Stack<int> stack, int gr, int z, int x, int comp)
    {
        if (z < 0 || z >= gr || x < 0 || x >= gr) return;
        if (!below[z, x] || label[z, x] != 0) return;
        label[z, x] = comp;
        stack.Push(z * gr + x);
    }

    // Maps a height to its 0..1 rank within the sorted set of all classification
    // heights, giving a uniform, terrain-relative elevation value.
    private static float Percentile(float[] sorted, float value)
    {
        if (sorted.Length <= 1) return 0.5f;
        int i = System.Array.BinarySearch(sorted, value);
        if (i < 0) i = ~i;
        return Mathf.Clamp01((float)i / (sorted.Length - 1));
    }

    // Unity terrain heightmaps must be (2^n)+1. Snap to the nearest supported value.
    private static int SnapHeightmapResolution(int requested)
    {
        int[] valid = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };
        int best = valid[0];
        int bestDist = Mathf.Abs(requested - best);
        for (int i = 1; i < valid.Length; i++)
        {
            int d = Mathf.Abs(requested - valid[i]);
            if (d < bestDist) { best = valid[i]; bestDist = d; }
        }
        return best;
    }

    private float SampleNoise(float nx, float nz, NoiseSettings settings, Vector2 seedOffset, int octaveOverride = -1)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f;
        float fScale = Mathf.Max(0.01f, settings.featureScale);
        int octaves = octaveOverride > 0 ? Mathf.Min(octaveOverride, settings.octaves) : settings.octaves;

        for (int i = 0; i < octaves; i++)
        {
            // Normalized coords (0..1) scaled by feature count -> map size independent,
            // so a larger physical map yields larger, smoother biomes.
            float sx = (nx * fScale * frequency) + settings.offset.x + seedOffset.x;
            float sz = (nz * fScale * frequency) + settings.offset.y + seedOffset.y;
            value += Mathf.PerlinNoise(sx, sz) * amplitude;
            maxValue += amplitude;
            amplitude *= settings.persistence;
            frequency *= settings.lacunarity;
        }

        // Redistribute toward the full 0..1 range for stronger height/biome variety.
        float normalized = value / maxValue;
        normalized = Mathf.Clamp01((normalized - 0.5f) * 1.6f + 0.5f);
        return normalized;
    }
}
