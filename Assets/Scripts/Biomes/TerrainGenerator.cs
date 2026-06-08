using UnityEngine;

[System.Serializable] public class NoiseSettings { [Tooltip("How many noise features span the whole map. Lower = larger, smoother regions.")] public float featureScale = 3f; [Range(1, 8)] public int octaves = 4; [Range(0f, 1f)] public float persistence = 0.5f; public float lacunarity = 2f; public Vector2 offset; }

public class TerrainGenerator : MonoBehaviour
{
    [Header("Map Size")][Tooltip("Physical map size in kilometers (square). Bigger map = larger, smoother biomes.")] public float mapSizeKm = 20f;

    [Tooltip("Maximum terrain height in meters.")]
    public float terrainHeight = 2000f;

    [Tooltip("Height curve exponent for the visual terrain. >1 flattens lowlands into broad plains and keeps mountains as rare, large landforms (more realistic). Does not affect biome classification.")]
    [Range(1f, 4f)] public float heightRedistribution = 2.5f;

    [Tooltip("Heightmap resolution. Snapped to a valid value: 513, 1025, 2049 or 4097. Higher = more detail but slower.")]
    public int heightmapResolution = 2049;

    [Tooltip("Resolution of the generated biome color map texture.")]
    public int colormapResolution = 1024;

    [Tooltip("Octaves used when classifying biomes by height. Low = large, smooth biome regions (terrain detail is unaffected).")]
    [Range(1, 5)] public int biomeHeightOctaves = 2;

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

    [Tooltip("How irregular the border mountains are (0 = straight wall, 1 = very meandering/natural).")]
    [Range(0f, 1f)] public float borderRoughness = 0.6f;

    private Vector2 _borderOffset;

    public float TerrainSizeMeters => Mathf.Max(1f, mapSizeKm) * 1000f;

    public Terrain GenerateTerrain(int seed, BiomeMap biomeMap)
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
        _borderOffset = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);

        // 1) Build the biome map at its own (low) resolution.
        int bres = biomeMap.Resolution;
        for (int z = 0; z < bres; z++)
        {
            float nz = (float)z / (bres - 1);
            for (int x = 0; x < bres; x++)
            {
                float nx = (float)x / (bres - 1);
                // Use a smoothed (low-octave) height for biome classification so
                // regions stay large; the detailed heightmap is built separately below.
                float h = SampleNoise(nx, nz, heightNoise, heightOff, biomeHeightOctaves);
                h = ApplyBorder(h, nx, nz);
                float t = SampleNoise(nx, nz, temperatureNoise, tempOff);
                float hum = SampleNoise(nx, nz, humidityNoise, humOff);
                biomeMap.SetPoint(x, z, h, t, hum);
            }
        }

        // 2) Build the heightmap at full resolution (height noise only).
        float[,] heights = new float[res, res];
        for (int z = 0; z < res; z++)
        {
            float nz = (float)z / (res - 1);
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);
                // Power curve: broad flat lowlands, mountains stay as rare large landforms.
                float baseH = Mathf.Pow(SampleNoise(nx, nz, heightNoise, heightOff), heightRedistribution);
                heights[z, x] = ApplyBorder(baseH, nx, nz);
            }
        }
        data.SetHeights(0, 0, heights);

        ApplySplatmap(data, biomeMap, sizeMeters);

        GameObject terrainObj = Terrain.CreateTerrainGameObject(data);
        terrainObj.name = "GeneratedTerrain";
        return terrainObj.GetComponent<Terrain>();
    }

    private void ApplySplatmap(TerrainData data, BiomeMap biomeMap, float sizeMeters)
    {
        int res = Mathf.Clamp(Mathf.ClosestPowerOfTwo(colormapResolution), 256, 2048);
        data.alphamapResolution = res;

        TerrainLayer layer = new TerrainLayer();
        layer.diffuseTexture = Texture2D.whiteTexture;
        layer.tileSize = new Vector2(sizeMeters, sizeMeters);
        // Fully matte terrain: kill the specular sheen / glossy highlights.
        layer.smoothness = 0f;
        layer.metallic = 0f;
        layer.specular = Color.black;
        data.terrainLayers = new TerrainLayer[] { layer };

        float[,,] alphas = new float[res, res, 1];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                alphas[z, x, 0] = 1f;

        data.SetAlphamaps(0, 0, alphas);
        ApplyColormap(data, biomeMap, res);
    }

    public static void ApplyColormap(TerrainData data, BiomeMap biomeMap, int res)
    {
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
                pixels[z * res + x] = biomeMap.GetColor(bx, bz);
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