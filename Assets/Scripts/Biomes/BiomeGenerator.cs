using UnityEngine;

[RequireComponent(typeof(TerrainGenerator))]
public class BiomeGenerator : MonoBehaviour
{
    [Header("Generation")]
    public int seed = 42;
    public int biomeMapResolution = 256;

    [Header("Biomes")]
    public BiomeData[] biomes;

    [Tooltip("Blend band width (0..1) for elevation biomes (water/mountains/snow). Higher = softer, wider transitions.")]
    [Range(0f, 0.6f)] public float biomeBlend = 0.14f;

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

        generatedTerrain = _terrainGen.GenerateTerrain(seed, biomes, biomeBlend, biomeMapResolution, out biomeMap);
        generatedTerrain.transform.position = Vector3.zero;

        // Scatter trees / rocks / grass / sand per biome, if a scatterer is present.
        var scatterer = GetComponent<BiomeScatterer>();
        if (scatterer != null)
            scatterer.Scatter(generatedTerrain, biomeMap, seed);
    }
}
