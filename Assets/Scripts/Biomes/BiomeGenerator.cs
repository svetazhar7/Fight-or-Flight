using UnityEngine;

[RequireComponent(typeof(TerrainGenerator))]
public class BiomeGenerator : MonoBehaviour
{
    [Header("Generation")]
    public int seed = 42;
    public int biomeMapResolution = 256;

    [Header("Biomes")]
    public BiomeData[] biomes;

    [Tooltip("Blend band width (0..1). Higher = softer, wider transitions between biomes.")]
    [Range(0f, 0.4f)] public float biomeBlend = 0.12f;

    [HideInInspector] public BiomeMap biomeMap;
    [HideInInspector] public Terrain generatedTerrain;

    private TerrainGenerator _terrainGen;

    void Awake()
    {
        Generate();
    }

    public void Generate()
    {
        _terrainGen = GetComponent<TerrainGenerator>();

        if (generatedTerrain != null)
            DestroyImmediate(generatedTerrain.gameObject);

        biomeMap = new BiomeMap(biomeMapResolution, biomes, biomeBlend);
        generatedTerrain = _terrainGen.GenerateTerrain(seed, biomeMap);
        generatedTerrain.transform.position = Vector3.zero;

        // Scatter trees / rocks / grass / sand per biome, if a scatterer is present.
        var scatterer = GetComponent<BiomeScatterer>();
        if (scatterer != null)
            scatterer.Scatter(generatedTerrain, biomeMap, seed);
    }
}
