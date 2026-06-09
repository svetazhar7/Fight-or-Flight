using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scatters trees, rocks and ground detail (grass / sand) onto a generated
/// Terrain, restricted to the matching biome:
///   Forest    -> trees
///   Mountains -> rocks
///   Plains    -> grass detail
///   Desert    -> sand detail
/// Uses Unity Terrain trees (TreeInstance) and detail layers for performance.
/// </summary>
public class BiomeScatterer : MonoBehaviour
{
    [Header("Prefabs (assigned automatically by the setup tool)")]
    public GameObject treePrefab;
    public GameObject rockPrefab;

    [Header("Trees (Forest)")]
    public string forestBiome = "Forest";
    public int treeCount = 6000;
    public Vector2 treeScale = new Vector2(0.8f, 1.4f);
    [Tooltip("Skip slopes steeper than this (degrees).")]
    public float treeMaxSlope = 28f;

    [Header("Rocks (Mountains)")]
    public string mountainBiome = "Mountains";
    public int rockCount = 2500;
    public Vector2 rockScale = new Vector2(0.6f, 2.2f);
    public float rockMaxSlope = 55f;

    [Header("Grass detail (Plains)")]
    public string plainsBiome = "Plains";
    public bool grassEnabled = true;
    [Range(1, 16)] public int grassDensity = 12;
    public Color grassHealthy = new Color(0.45f, 0.62f, 0.27f);
    public Color grassDry = new Color(0.62f, 0.6f, 0.3f);

    [Header("Sand detail (Desert)")]
    public string desertBiome = "Desert";
    public bool sandEnabled = true;
    [Range(1, 16)] public int sandDensity = 9;
    public Color sandColor = new Color(0.85f, 0.76f, 0.5f);

    [Header("Detail settings")]
    public int detailResolution = 1024;
    public int detailPerPatch = 32;
    [Tooltip("Max distance grass/sand detail is drawn (m).")]
    public float detailDistance = 180f;
    [Tooltip("Max distance trees/rocks are drawn (m).")]
    public float treeDistance = 5000f;

    private Texture2D _grassTex;
    private Texture2D _sandTex;

    public void Scatter(Terrain terrain, BiomeMap map, int seed)
    {
        if (terrain == null || map == null) return;
        TerrainData data = terrain.terrainData;
        System.Random rng = new System.Random(seed * 7919 + 13);

        ScatterTreesAndRocks(terrain, data, map, rng);
        ScatterDetails(terrain, data, map);
    }

    // ---------------- Trees & Rocks ----------------

    private void ScatterTreesAndRocks(Terrain terrain, TerrainData data, BiomeMap map, System.Random rng)
    {
        var protos = new List<TreePrototype>();
        int treeIdx = -1, rockIdx = -1;
        if (treePrefab != null) { treeIdx = protos.Count; protos.Add(new TreePrototype { prefab = treePrefab }); }
        if (rockPrefab != null) { rockIdx = protos.Count; protos.Add(new TreePrototype { prefab = rockPrefab }); }

        data.treePrototypes = protos.ToArray();
        data.RefreshPrototypes();

        var instances = new List<TreeInstance>();
        PlaceProto(instances, treeIdx, forestBiome, treeCount, treeScale, treeMaxSlope, data, map, rng);
        PlaceProto(instances, rockIdx, mountainBiome, rockCount, rockScale, rockMaxSlope, data, map, rng);
        data.SetTreeInstances(instances.ToArray(), true);

        terrain.treeDistance = treeDistance;
        // No Nature/Soft-Occlusion billboard shader on these mesh prefabs, so render
        // them as full meshes as far out as possible (max 2000 m) to avoid pop-out.
        terrain.treeBillboardDistance = 2000f;
        terrain.treeCrossFadeLength = 50f;
        terrain.treeMaximumFullLODCount = 10000;
    }

    private void PlaceProto(List<TreeInstance> list, int protoIndex, string biomeName, int count,
        Vector2 scale, float maxSlope, TerrainData data, BiomeMap map, System.Random rng)
    {
        if (protoIndex < 0 || count <= 0) return;
        int res = map.Resolution;
        int placed = 0, attempts = 0, maxAttempts = count * 40;

        while (placed < count && attempts < maxAttempts)
        {
            attempts++;
            float u = (float)rng.NextDouble();
            float v = (float)rng.NextDouble();
            int bx = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 0, res - 1);
            int bz = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 0, res - 1);
            if (map.IsWater(bx, bz)) continue; // don't plant trees/rocks in lakes or rivers
            BiomeData b = map.GetDominantBiome(bx, bz);
            if (b == null || b.biomeName != biomeName) continue;
            if (data.GetSteepness(u, v) > maxSlope) continue;

            float s = Mathf.Lerp(scale.x, scale.y, (float)rng.NextDouble());
            list.Add(new TreeInstance
            {
                prototypeIndex = protoIndex,
                position = new Vector3(u, 0f, v),
                heightScale = s,
                widthScale = s * Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble()),
                rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                color = Color.white,
                lightmapColor = Color.white
            });
            placed++;
        }
    }

    // ---------------- Ground detail ----------------

    private void ScatterDetails(Terrain terrain, TerrainData data, BiomeMap map)
    {
        int dres = Mathf.Clamp(detailResolution, 128, 2048);
        data.SetDetailResolution(dres, Mathf.Clamp(detailPerPatch, 8, 128));

        var dprotos = new List<DetailPrototype>();
        int grassLayer = -1, sandLayer = -1;

        if (grassEnabled)
        {
            grassLayer = dprotos.Count;
            _grassTex = MakeBladeTexture(grassHealthy);
            dprotos.Add(MakeGrassPrototype(_grassTex, grassHealthy, grassDry, 1.0f, 2.0f, 0.7f, 1.5f));
        }
        if (sandEnabled)
        {
            sandLayer = dprotos.Count;
            _sandTex = MakeTuftTexture(sandColor);
            dprotos.Add(MakeGrassPrototype(_sandTex, sandColor, sandColor * 0.9f, 0.7f, 1.3f, 0.3f, 0.6f));
        }

        data.detailPrototypes = dprotos.ToArray();

        if (grassLayer >= 0) FillDetailLayer(data, dres, grassLayer, plainsBiome, grassDensity, map);
        if (sandLayer >= 0) FillDetailLayer(data, dres, sandLayer, desertBiome, sandDensity, map);

        terrain.detailObjectDistance = Mathf.Clamp(detailDistance, 0f, 250f);
    }

    private void FillDetailLayer(TerrainData data, int dres, int layer, string biomeName, int density, BiomeMap map)
    {
        int res = map.Resolution;
        int[,] dm = new int[dres, dres];
        for (int zi = 0; zi < dres; zi++)
        {
            float v = (float)zi / (dres - 1);
            int bz = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 0, res - 1);
            for (int xi = 0; xi < dres; xi++)
            {
                float u = (float)xi / (dres - 1);
                int bx = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 0, res - 1);
                BiomeData b = map.GetDominantBiome(bx, bz);
                bool ok = !map.IsWater(bx, bz) && b != null && b.biomeName == biomeName;
                dm[zi, xi] = ok ? density : 0;
            }
        }
        data.SetDetailLayer(0, 0, layer, dm);
    }

    private static DetailPrototype MakeGrassPrototype(Texture2D tex, Color healthy, Color dry,
        float minW, float maxW, float minH, float maxH)
    {
        return new DetailPrototype
        {
            usePrototypeMesh = false,
            prototypeTexture = tex,
            renderMode = DetailRenderMode.GrassBillboard,
            healthyColor = healthy,
            dryColor = dry,
            minWidth = minW,
            maxWidth = maxW,
            minHeight = minH,
            maxHeight = maxH,
            noiseSpread = 0.4f
        };
    }

    // A blade-cluster billboard (white; tinted by the detail prototype color).
    private static Texture2D MakeBladeTexture(Color c)
    {
        int s = 32;
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color[s * s];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, 0f);

        // five tapering vertical blades for a fuller tuft
        float[] centers = { 0.18f, 0.34f, 0.5f, 0.66f, 0.82f };
        foreach (float cx in centers)
        {
            int baseX = Mathf.RoundToInt(cx * s);
            for (int y = 0; y < s; y++)
            {
                float tnorm = (float)y / (s - 1);          // 0 bottom -> 1 top
                int halfW = Mathf.Max(0, Mathf.RoundToInt((1f - tnorm) * 2f)); // taper to tip
                for (int dx = -halfW; dx <= halfW; dx++)
                {
                    int x = baseX + dx;
                    if (x < 0 || x >= s) continue;
                    float shade = Mathf.Lerp(0.8f, 1f, tnorm);
                    px[y * s + x] = new Color(shade, shade, shade, 1f);
                }
            }
        }
        t.SetPixels(px);
        t.Apply();
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }

    // A short, wide tuft for sandy / dry detail (white; tinted by prototype color).
    private static Texture2D MakeTuftTexture(Color c)
    {
        int s = 32;
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color[s * s];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, 0f);

        for (int x = 0; x < s; x++)
        {
            // ground-hugging mound: tallest at the centre
            float fx = (x / (float)(s - 1)) * 2f - 1f;       // -1..1
            int top = Mathf.RoundToInt(Mathf.Lerp(s * 0.55f, s * 0.05f, Mathf.Abs(fx)));
            for (int y = 0; y < top; y++)
            {
                float shade = Mathf.Lerp(0.85f, 1f, (float)y / Mathf.Max(1, top));
                px[y * s + x] = new Color(shade, shade, shade, 1f);
            }
        }
        t.SetPixels(px);
        t.Apply();
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }
}
