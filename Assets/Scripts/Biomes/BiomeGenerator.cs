using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Drives procedural terrain generation and keeps it consistent across the
/// network. Terrain generation is fully deterministic from a single integer
/// seed, so instead of streaming the (huge) heightmap over the wire we only
/// sync the seed: the server picks it, every client receives it, and each peer
/// builds the identical world locally. The server builds it too (its
/// TerrainCollider is needed for authoritative physics, even headless).
/// </summary>
[RequireComponent(typeof(TerrainGenerator))]
public class BiomeGenerator : NetworkBehaviour
{
    [Header("Generation")]
    [Tooltip("Seed used offline and as the fallback. In multiplayer the server's seed is synced to everyone so the world is identical.")]
    public int seed = 42;

    [Tooltip("Server picks a fresh random seed on startup so each match gets a different world. Off = always use 'seed'.")]
    public bool randomizeSeedOnServer = false;

    [Tooltip("Generate immediately on play even without a network (for the offline test scene). Leave OFF on networked scenes.")]
    public bool autoGenerateOffline = false;

    public int biomeMapResolution = 256;

    [Header("Biomes")]
    public BiomeData[] biomes;

    [Tooltip("Blend band width (0..1) for elevation biomes (water/mountains/snow). Higher = softer, wider transitions.")]
    [Range(0f, 0.6f)] public float biomeBlend = 0.14f;

    [HideInInspector] public BiomeMap biomeMap;

    /// <summary>Generated world data (heightmap, colormap, biomes). The visual
    /// terrain itself is streamed in chunks by WorldStreamingManager.</summary>
    [HideInInspector] public WorldData worldData;

    // Server-authoritative world seed, synced to every client with the spawn so
    // all peers generate byte-for-byte the same terrain.
    private readonly SyncVar<int> _seed = new();

    private TerrainGenerator _terrainGen;
    private bool _generated;
    private int _worldSeed;

    public int ActiveSeed => _worldSeed;

    public override void OnStartServer()
    {
        base.OnStartServer();
        _seed.Value = randomizeSeedOnServer
            ? new System.Random().Next(int.MinValue, int.MaxValue)
            : seed;
        // Build on the server too (needed for the collider / server physics).
        TryGenerate(_seed.Value);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // The seed is delivered with the spawn payload, so it's valid here. On a
        // host this is a no-op because OnStartServer already generated.
        TryGenerate(_seed.Value);
    }

    private void Start()
    {
        // Offline preview (e.g. the Terrain test scene) with no network to drive us.
        if (autoGenerateOffline)
            TryGenerate(seed);
    }

    private void TryGenerate(int worldSeed)
    {
        if (_generated) return;
        _generated = true;
        _worldSeed = worldSeed;
        GenerateFromSeed(worldSeed);
    }

    /// <summary>Regenerate using the inspector 'seed' (editor / single-player helper).</summary>
    public void Generate() => GenerateFromSeed(seed);

    public void GenerateFromSeed(int worldSeed)
    {
        _terrainGen = GetComponent<TerrainGenerator>();

        // 1) Deterministic data generation (identical on every peer).
        worldData = _terrainGen.GenerateWorldData(worldSeed, biomes, biomeBlend, biomeMapResolution, out biomeMap);

        // 2) Hand the data to the local-only chunk streaming system. Nothing
        //    visual is networked: each peer streams around its own players.
        // (FishNet QOL properties throw when the object isn't network-spawned,
        //  e.g. offline scenes or editor generation - treat that as offline.)
        bool isServer = false, isClient = false;
        try { isServer = IsServerStarted; isClient = IsClientStarted; } catch { }

        var streaming = GetComponent<WorldStreamingManager>();
        if (streaming == null) streaming = gameObject.AddComponent<WorldStreamingManager>();
        streaming.Initialize(worldData, _terrainGen, isServer, isClient);

        // 3) Scatter trees / rocks / grass per biome, if a scatterer is present.
        var scatterer = GetComponent<BiomeScatterer>();
        if (scatterer != null && streaming.WorldRoot != null)
            scatterer.Scatter(worldData, streaming.WorldRoot.transform, worldSeed);
    }
}
