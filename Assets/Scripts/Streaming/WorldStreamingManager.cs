using UnityEngine;

/// <summary>
/// Facade of the world streaming system. Lives next to BiomeGenerator on the
/// WorldGenerator object; receives the generated WorldData and from then on
/// runs a purely local (never networked) loop:
///
///   PlayerChunkTracker  - whom to stream around (local player; on the server
///                         also remote players, colliders only)
///   ChunkManager        - which chunks to load/unload, budgeted build pipeline
///   ChunkPool           - reusable Terrain objects (no Instantiate/Destroy churn)
///   ChunkLODController  - near/mid/far quality bands per chunk
///   TerrainQualityController - global terrain setup, camera far clip
///   FarTerrainRenderer  - one low-poly mesh of the whole map closing the horizon
///   StreamingDebugger   - gizmo visualization (separate component)
///
/// Every parameter is tunable in the Inspector; nothing here creates network
/// objects or sends messages.
/// </summary>
public class WorldStreamingManager : MonoBehaviour
{
    [Header("Chunk Grid")]
    [Tooltip("Desired chunk size in meters. Snapped so the map splits into a power-of-two grid that divides the global heightmap evenly (e.g. 20 km / 1250 m = 16x16 chunks of 129x129 samples).")]
    public float chunkSize = 1250f;

    [Header("Streaming Radii (in chunks)")]
    [Tooltip("Chunks within this radius of the local player are always loaded (full quality near ring).")]
    [Min(1)] public int loadRadius = 3;

    [Tooltip("Loaded chunks are released only beyond this radius. Must exceed Load Radius (hysteresis prevents load/unload flicker at the border).")]
    [Min(2)] public int unloadRadius = 5;

    [Tooltip("Extra ring beyond Load Radius that is fetched at low priority, so chunks are usually ready before the plane arrives.")]
    [Min(0)] public int preloadRadius = 1;

    [Tooltip("Hard cap on simultaneously loaded chunks (memory / draw call budget). Farthest pending chunks are dropped first.")]
    [Min(9)] public int maxLoadedChunks = 180;

    [Header("Altitude Adaptation")]
    [Tooltip("Radius multiplier vs. flight altitude above ground (m). Higher flight = wider visible ring of full terrain.")]
    public AnimationCurve altitudeRadiusCurve = new(
        new Keyframe(0f, 1f), new Keyframe(300f, 1.3f), new Keyframe(800f, 1.8f),
        new Keyframe(1500f, 2.4f), new Keyframe(3000f, 3.2f));

    [Tooltip("Safety cap for the altitude multiplier.")]
    public float maxRadiusMultiplier = 3.5f;

    [Header("Scheduling")]
    [Tooltip("Seconds between streaming decision ticks (what to load/unload, LOD bands). Builds themselves run every frame within the budget.")]
    [Range(0.05f, 2f)] public float updateInterval = 0.25f;

    [Tooltip("Maximum chunks whose heights are uploaded in a single frame.")]
    [Range(1, 8)] public int chunksPerFrame = 2;

    [Tooltip("Frame time budget (ms) for chunk building. The first chunk of a frame is always allowed so progress never stalls.")]
    [Range(0.5f, 16f)] public float generationBudgetMs = 4f;

    [Tooltip("Maximum chunks allowed to sit in the built-but-not-yet-activated stage.")]
    [Range(1, 16)] public int maxConcurrentJobs = 3;

    [Tooltip("Build the initial ring around players synchronously when the world is (re)generated, so the game never starts over a void.")]
    public bool bootstrapSynchronously = true;

    [Header("Pooling")]
    [Tooltip("Reuse Terrain objects instead of destroying them (strongly recommended: avoids GC spikes and TerrainData allocation).")]
    public bool enableChunkPooling = true;

    [Tooltip("Maximum inactive chunks kept in the pool.")]
    [Min(4)] public int chunkPoolSize = 48;

    [Header("Physics")]
    [Tooltip("Terrain colliders are enabled only within this radius (chunks) of a player - distant visual chunks skip PhysX baking.")]
    [Min(1)] public float colliderRadius = 2f;

    [Tooltip("Streaming radius (chunks) around OTHER players on the server, so their planes always have ground colliders. Visual quality is not affected.")]
    [Min(1)] public float remotePlayerRadius = 2f;

    [Header("Subsystems")]
    public PlayerChunkTracker tracker = new();
    public ChunkLODController lod = new();
    public TerrainQualityController quality = new();

    [Header("Debug")]
    [Tooltip("Draw chunk states, radii and counters as gizmos (see StreamingDebugger).")]
    public bool enableDebugVisualization = true;

    // --- runtime state ----------------------------------------------------
    private readonly ChunkPool _pool = new();
    private readonly ChunkManager _chunkManager = new();
    private WorldData _world;
    private GameObject _worldRoot;
    private bool _initialized;
    private bool _visualsEnabled = true;
    private float _tickTimer;

    public bool IsInitialized => _initialized;
    public WorldData World => _world;
    public GameObject WorldRoot => _worldRoot;
    public ChunkManager ChunkManagerInstance => _chunkManager;
    public ChunkPool PoolInstance => _pool;
    public int ChunksPerAxis { get; private set; }
    public float ChunkSizeActual { get; private set; }

    /// <summary>
    /// Build the streaming world from freshly generated data. Called by
    /// BiomeGenerator on server and on every client after the seed arrives.
    /// </summary>
    public void Initialize(WorldData world, TerrainGenerator terrainGen, bool isServer, bool isClient)
    {
        Shutdown();

        _world = world;
        // Dedicated server renders nothing; host and clients render normally.
        _visualsEnabled = isClient || !isServer;

        // Snap the chunk grid so it divides the heightmap exactly.
        int hmCells = world.heightRes - 1; // power of two (e.g. 2048)
        int wantAxis = Mathf.Clamp(Mathf.RoundToInt(world.worldSize.x / Mathf.Max(100f, chunkSize)), 2, 64);
        int axis = 2;
        while (axis * 2 <= wantAxis && hmCells % (axis * 2) == 0 && hmCells / (axis * 2) >= 32) axis *= 2;
        ChunksPerAxis = axis;
        ChunkSizeActual = world.worldSize.x / axis;
        int chunkRes = hmCells / axis + 1;

        _worldRoot = new GameObject("GeneratedWorld");
        _worldRoot.transform.position = Vector3.zero;

        _pool.Initialize(_worldRoot.transform, world, chunkRes, ChunkSizeActual, enableChunkPooling, chunkPoolSize);
        _chunkManager.Initialize(world, _pool, quality, _worldRoot.transform, _visualsEnabled,
            chunkRes, ChunkSizeActual, ChunksPerAxis);

        // Ocean plane + far horizon mesh: global, always-on, cheap.
        if (terrainGen != null && terrainGen.water.oceansEnabled)
            TerrainWaterBuilder.BuildOceanMesh(terrainGen.water.seaLevel, world.worldSize,
                terrainGen.water, terrainGen.oceanSubdivisions, _worldRoot.transform);

        if (_visualsEnabled)
            FarTerrainRenderer.Build(world, _worldRoot.transform, quality.farMeshResolution, quality.farMeshSinkMeters);

        _initialized = true;
        _tickTimer = 0f;

        // First tick right away so the world exists this frame.
        tracker.Refresh(999f, isServer, isClient);
        RefreshStreaming();
        if (bootstrapSynchronously) _chunkManager.ForceCompleteAll();
        lod.Apply(_chunkManager.Chunks);
    }

    public void Shutdown()
    {
        _initialized = false;
        _chunkManager.Clear();
        _pool.Clear();
        if (_worldRoot != null)
        {
            if (Application.isPlaying) Destroy(_worldRoot);
            else DestroyImmediate(_worldRoot);
        }
        _worldRoot = null;
        _world = null;
    }

    private void Update()
    {
        if (!_initialized) return;
        Tick(Time.deltaTime);
    }

    /// <summary>One streaming step. Public so tests/editor tools can drive it manually.</summary>
    public void Tick(float dt)
    {
        if (!_initialized) return;

        bool isServer = false, isClient = false;
        ResolveNetworkRole(ref isServer, ref isClient);

        tracker.Refresh(dt, isServer, isClient);

        _tickTimer -= dt;
        if (_tickTimer <= 0f)
        {
            _tickTimer = updateInterval;
            RefreshStreaming();
            lod.Apply(_chunkManager.Chunks);
            if (_visualsEnabled) quality.EnsureCameraFarClip();
        }

        // Never let a player hover over a missing chunk (teleports, spawns).
        var targets = tracker.Targets;
        for (int i = 0; i < targets.Count; i++)
            if (targets[i].transform != null)
                _chunkManager.EnsureChunkUnder(targets[i].transform.position);

        _chunkManager.ProcessBuilds(generationBudgetMs, chunksPerFrame, maxConcurrentJobs);
        _chunkManager.ProcessUnloads(4);
    }

    private void RefreshStreaming()
    {
        var cfg = new ChunkManager.StreamConfig
        {
            loadRadiusChunks = loadRadius,
            unloadRadiusChunks = Mathf.Max(unloadRadius, loadRadius + 1),
            preloadRadiusChunks = preloadRadius,
            maxLoadedChunks = maxLoadedChunks,
            remoteRadiusChunks = remotePlayerRadius,
            colliderRadiusChunks = colliderRadius,
            altitudeCurve = altitudeRadiusCurve,
            maxRadiusMultiplier = maxRadiusMultiplier,
        };
        _chunkManager.Refresh(tracker.Targets, cfg);
    }

    private static void ResolveNetworkRole(ref bool isServer, ref bool isClient)
    {
        try
        {
            isServer = FishNet.InstanceFinder.IsServerStarted;
            isClient = FishNet.InstanceFinder.IsClientStarted;
        }
        catch
        {
            isServer = false;
            isClient = false;
        }
    }

    private void OnDestroy() => Shutdown();
}
