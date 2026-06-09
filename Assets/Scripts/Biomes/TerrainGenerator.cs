using System.Collections.Generic;
using UnityEngine;

[System.Serializable] public class NoiseSettings { [Tooltip("How many noise features span the whole map. Lower = larger, smoother regions.")] public float featureScale = 3f; [Range(1, 8)] public int octaves = 4; [Range(0f, 1f)] public float persistence = 0.5f; public float lacunarity = 2f; public Vector2 offset; }

public class TerrainGenerator : MonoBehaviour
{
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

    [Header("Water (lakes & rivers)")]
    public TerrainWaterBuilder.Settings water = new TerrainWaterBuilder.Settings
    {
        lakesEnabled = true,
        lakeCount = 8,
        lakeRadius = 0.02f,
        lakeDepth = 0.012f,
        lakeMaxTerrain = 0.35f,

        riversEnabled = true,
        riverCount = 5,
        riverDepth = 0.01f,
        riverWidth = 2,
        riverSourceMinHeight = 0.6f,
        riverMeander = 0.6f,
        riverMaxSteps = 4000,

        waterShallow = new Color(0.30f, 0.55f, 0.62f),
        waterDeep = new Color(0.10f, 0.32f, 0.50f)
    };

    [Tooltip("Resolution of the generated water surface mesh (kept coarse for performance).")]
    [Range(32, 512)] public int waterMeshResolution = 257;

    [Tooltip("How irregular the border mountains are (0 = straight wall, 1 = very meandering/natural).")]
    [Range(0f, 1f)] public float borderRoughness = 0.6f;

    private Vector2 _borderOffset;
    private Vector2 _grainOffset;

    public float TerrainSizeMeters => Mathf.Max(1f, mapSizeKm) * 1000f;

    public Terrain GenerateTerrain(int seed, BiomeData[] biomes, float biomeBlend, int biomeMapResolution, out BiomeMap biomeMap)
    {
        float sizeMeters = TerrainSizeMeters;
        int res = SnapHeightmapResolution(heightmapResolution);

        TerrainData data = new TerrainData();
        data.heightmapResolution = res;
        data.size = new Vector3(sizeMeters, terrainHeight, sizeMeters);

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
        //    'flow' keeps the smooth base shape (no fine detail) so rivers can find
        //    a consistent downhill path without snagging on noise pits.
        float[,] heights = new float[res, res];
        float[,] flow = new float[res, res];
        for (int z = 0; z < res; z++)
        {
            float nz = (float)z / (res - 1);
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);

                // Power curve: broad flat lowlands, mountains stay as rare large landforms.
                float baseH = Mathf.Pow(SampleNoise(nx, nz, heightNoise, heightOff), heightRedistribution);
                baseH = ApplyBorder(baseH, nx, nz);
                flow[z, x] = baseH;

                heights[z, x] = Mathf.Clamp01(ApplyRuggedness(baseH, nx, nz, detailOff));
            }
        }

        // 2) Carve lakes and rivers, then commit the heights.
        TerrainWaterBuilder.Result waterResult =
            TerrainWaterBuilder.Carve(heights, flow, res, water, new System.Random(seed * 101 + 5));
        data.SetHeights(0, 0, heights);

        // 3) Build the biome map at its own (low) resolution.
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
                rawH[z * bres + x] = ApplyBorder(h, nx, nz);
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

                // Flag water at biome-map granularity so scatter can skip it.
                int hx = Mathf.Clamp(Mathf.RoundToInt(nx * (res - 1)), 0, res - 1);
                int hz = Mathf.Clamp(Mathf.RoundToInt(nz * (res - 1)), 0, res - 1);
                if (waterResult.surface[hz, hx] > TerrainWaterBuilder.Result.NoWater + 0.5f)
                    biomeMap.SetWater(x, z, true);
            }
        }

        ApplySplatmap(data, biomeMap, sizeMeters, heights, waterResult, res);

        GameObject terrainObj = Terrain.CreateTerrainGameObject(data);
        terrainObj.name = "GeneratedTerrain";

        // 4) Build the water surface mesh as a child of the terrain.
        TerrainWaterBuilder.BuildWaterMesh(waterResult, res, data.size, water, waterMeshResolution, terrainObj.transform);

        return terrainObj.GetComponent<Terrain>();
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

    private void ApplySplatmap(TerrainData data, BiomeMap biomeMap, float sizeMeters,
        float[,] heights, TerrainWaterBuilder.Result water, int heightRes)
    {
        int res = Mathf.Clamp(Mathf.ClosestPowerOfTwo(colormapResolution), 256, 2048);
        data.alphamapResolution = res;

        TerrainLayer layer = new TerrainLayer();
        layer.diffuseTexture = Texture2D.whiteTexture;
        layer.tileSize = new Vector2(sizeMeters, sizeMeters);
        // Fully matte terrain: kill the specular sheen / glossy highlights so the
        // ground reads as rough and earthy rather than wet plastic.
        layer.smoothness = 0f;
        layer.metallic = 0f;
        layer.specular = Color.black;
        data.terrainLayers = new TerrainLayer[] { layer };

        float[,,] alphas = new float[res, res, 1];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                alphas[z, x, 0] = 1f;

        data.SetAlphamaps(0, 0, alphas);
        ApplyColormap(data, biomeMap, res, heights, water, heightRes);
    }

    private void ApplyColormap(TerrainData data, BiomeMap biomeMap, int res,
        float[,] heights, TerrainWaterBuilder.Result water, int heightRes)
    {
        Texture2D colorTex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        colorTex.filterMode = FilterMode.Bilinear;
        colorTex.wrapMode = TextureWrapMode.Clamp;

        float waterDepthRef = Mathf.Max(0.001f, Mathf.Max(this.water.lakeDepth, this.water.riverDepth));

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

                // Water tint from the carved lake/river field.
                if (water != null && water.surface != null)
                {
                    int hx = Mathf.Clamp(Mathf.RoundToInt((float)x / (res - 1) * (heightRes - 1)), 0, heightRes - 1);
                    int hz = Mathf.Clamp(Mathf.RoundToInt((float)z / (res - 1) * (heightRes - 1)), 0, heightRes - 1);
                    float surf = water.surface[hz, hx];
                    if (surf > TerrainWaterBuilder.Result.NoWater + 0.5f)
                    {
                        float bed = heights[hz, hx];
                        float depth = Mathf.Clamp01((surf - bed) / waterDepthRef);
                        Color wcol = Color.Lerp(this.water.waterShallow, this.water.waterDeep, depth);
                        c = Color.Lerp(c, wcol, Mathf.Lerp(0.55f, 0.95f, depth));
                    }
                }

                // URP Terrain Lit reads smoothness from the base map's alpha. Keep it
                // near 0 so the ground is matte/earthy instead of wet-looking plastic.
                c.a = 0f;
                pixels[z * res + x] = c;
            }
        }

        colorTex.SetPixels(pixels);
        colorTex.Apply();

        TerrainLayer[] layers = data.terrainLayers;
        if (layers.Length > 0)
        {
            layers[0].diffuseTexture = colorTex;
            data.terrainLayers = layers;
        }
    }

    // Raises height toward borderPeakHeight near the map edges, forming a tall
    // boundary ridge. edgeDist is 0 at the very edge and 0.5 at the map center.
    private float ApplyBorder(float h, float nx, float nz)
    {
        if (!borderMountains) return h;

        float edgeDist = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(nz, 1f - nz));
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
