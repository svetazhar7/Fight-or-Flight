using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Owns the set of streamed terrain chunks: decides which cells are wanted
/// around the tracked players, queues loads by priority (nearest first),
/// schedules unloads with hysteresis, and advances the build pipeline under a
/// per-frame millisecond budget so crossing chunk borders never hitches.
/// All collections are reused between ticks - steady-state flight allocates
/// nothing.
/// </summary>
public class ChunkManager
{
    public struct StreamConfig
    {
        public int loadRadiusChunks;
        public int unloadRadiusChunks;
        public int preloadRadiusChunks;
        public int maxLoadedChunks;
        public float remoteRadiusChunks;     // streaming radius around other players (server colliders)
        public float colliderRadiusChunks;   // colliders only this close to a target
        public AnimationCurve altitudeCurve; // flight altitude -> radius multiplier
        public float maxRadiusMultiplier;
    }

    private const float PreloadPenalty = 1e6f; // preload entries sort after every core entry

    private readonly Dictionary<ChunkCoord, TerrainChunk> _chunks = new();
    private readonly List<TerrainChunk> _buildQueue = new();
    private readonly List<TerrainChunk> _builtJobs = new();
    private readonly List<TerrainChunk> _unloadQueue = new();
    private readonly List<ChunkCoord> _tmpRemove = new();
    private readonly List<Vector3> _tmpTargetPos = new();
    private readonly List<float> _tmpUnloadR = new();
    private readonly Stopwatch _sw = new();
    private static readonly System.Comparison<TerrainChunk> ByPriority =
        (a, b) => a.priority.CompareTo(b.priority);

    private WorldData _world;
    private ChunkPool _pool;
    private TerrainQualityController _quality;
    private Transform _root;
    private bool _visualsEnabled;
    private int _chunkRes;
    private float _chunkSize;
    private int _chunksPerAxis;
    private float[,] _scratch;

    public int ActiveCount { get; private set; }
    public int QueuedCount => _buildQueue.Count;
    public int PendingUnloadCount => _unloadQueue.Count;
    public Dictionary<ChunkCoord, TerrainChunk>.ValueCollection Chunks => _chunks.Values;
    public float ChunkSize => _chunkSize;

    public void Initialize(WorldData world, ChunkPool pool, TerrainQualityController quality,
        Transform root, bool visualsEnabled, int chunkRes, float chunkSize, int chunksPerAxis)
    {
        _world = world;
        _pool = pool;
        _quality = quality;
        _root = root;
        _visualsEnabled = visualsEnabled;
        _chunkRes = chunkRes;
        _chunkSize = chunkSize;
        _chunksPerAxis = chunksPerAxis;
        _scratch = new float[chunkRes, chunkRes];
        Clear();
    }

    public ChunkCoord CoordAt(Vector3 worldPos) => new(
        Mathf.Clamp(Mathf.FloorToInt(worldPos.x / _chunkSize), 0, _chunksPerAxis - 1),
        Mathf.Clamp(Mathf.FloorToInt(worldPos.z / _chunkSize), 0, _chunksPerAxis - 1));

    /// <summary>Recompute the wanted set and (re)fill the load/unload queues.</summary>
    public void Refresh(IReadOnlyList<PlayerChunkTracker.Target> targets, in StreamConfig cfg)
    {
        // Reset per-tick desire flags by marking distances stale.
        foreach (TerrainChunk c in _chunks.Values)
        {
            c.distance = float.MaxValue;
            c.priority = float.MaxValue;
        }

        _tmpTargetPos.Clear();
        _tmpUnloadR.Clear();

        // --- mark wanted cells around every target ---------------------------
        for (int ti = 0; ti < targets.Count; ti++)
        {
            Transform tr = targets[ti].transform;
            if (tr == null) continue;
            Vector3 pos = tr.position;
            bool local = targets[ti].isLocal;

            float altitude = Mathf.Max(0f, pos.y - _world.SampleHeightWorld(pos.x, pos.z));
            float mult = 1f;
            if (local && cfg.altitudeCurve != null)
                mult = Mathf.Clamp(cfg.altitudeCurve.Evaluate(altitude), 1f, Mathf.Max(1f, cfg.maxRadiusMultiplier));

            float loadR = (local ? cfg.loadRadiusChunks : cfg.remoteRadiusChunks) * _chunkSize * mult;
            float preR = local ? loadR + cfg.preloadRadiusChunks * _chunkSize : loadR;
            float colR = local ? cfg.colliderRadiusChunks * _chunkSize : loadR;
            float unloadR = (local ? cfg.unloadRadiusChunks : cfg.remoteRadiusChunks + 1f) * _chunkSize * mult;

            _tmpTargetPos.Add(pos);
            _tmpUnloadR.Add(unloadR);

            int cx0 = Mathf.Clamp(Mathf.FloorToInt((pos.x - preR) / _chunkSize), 0, _chunksPerAxis - 1);
            int cx1 = Mathf.Clamp(Mathf.FloorToInt((pos.x + preR) / _chunkSize), 0, _chunksPerAxis - 1);
            int cz0 = Mathf.Clamp(Mathf.FloorToInt((pos.z - preR) / _chunkSize), 0, _chunksPerAxis - 1);
            int cz1 = Mathf.Clamp(Mathf.FloorToInt((pos.z + preR) / _chunkSize), 0, _chunksPerAxis - 1);

            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    float dx = (cx + 0.5f) * _chunkSize - pos.x;
                    float dz = (cz + 0.5f) * _chunkSize - pos.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > preR) continue;
                    bool preloadOnly = d > loadR;

                    var coord = new ChunkCoord(cx, cz);
                    if (!_chunks.TryGetValue(coord, out TerrainChunk chunk))
                    {
                        chunk = new TerrainChunk { coord = coord, state = ChunkState.Queued };
                        _chunks.Add(coord, chunk);
                        _buildQueue.Add(chunk);
                        chunk.isPreload = preloadOnly;
                    }

                    float prio = preloadOnly ? d + PreloadPenalty : d;
                    if (d < chunk.distance) chunk.distance = d;
                    if (prio < chunk.priority) chunk.priority = prio;
                    if (!preloadOnly) chunk.isPreload = false;
                    if (d <= colR) chunk.wantCollider = true;

                    // A chunk that was on its way out is wanted again - cheap revive.
                    if (chunk.state == ChunkState.PendingUnload)
                        chunk.state = ChunkState.Active;
                }
            }
        }

        // --- schedule unloads (hysteresis: must leave every unload radius) ---
        _tmpRemove.Clear();
        foreach (TerrainChunk c in _chunks.Values)
        {
            if (c.priority < float.MaxValue) continue; // still wanted

            bool keep = false;
            float cxw = (c.coord.x + 0.5f) * _chunkSize;
            float czw = (c.coord.z + 0.5f) * _chunkSize;
            for (int i = 0; i < _tmpTargetPos.Count; i++)
            {
                float dx = cxw - _tmpTargetPos[i].x;
                float dz = czw - _tmpTargetPos[i].z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d <= _tmpUnloadR[i]) { keep = true; if (d < c.distance) c.distance = d; break; }
            }
            if (keep) continue;

            switch (c.state)
            {
                case ChunkState.Queued:
                    _tmpRemove.Add(c.coord); // never built - just forget it
                    break;
                case ChunkState.Active:
                    // (Built chunks are left alone: they activate first and get
                    // unloaded on a later tick - keeps the bookkeeping simple.)
                    c.state = ChunkState.PendingUnload;
                    _unloadQueue.Add(c);
                    break;
            }
        }
        foreach (ChunkCoord coord in _tmpRemove)
        {
            if (_chunks.TryGetValue(coord, out TerrainChunk c) && c.state == ChunkState.Queued)
                _chunks.Remove(coord); // stale entries in _buildQueue are skipped on pop
        }

        // --- cap total loaded chunks: trim farthest queued entries -----------
        CompactBuildQueue();
        _buildQueue.Sort(ByPriority);
        int budgetLeft = cfg.maxLoadedChunks - (ActiveCount + _builtJobs.Count);
        if (_buildQueue.Count > Mathf.Max(0, budgetLeft))
        {
            for (int i = _buildQueue.Count - 1; i >= Mathf.Max(0, budgetLeft); i--)
            {
                TerrainChunk c = _buildQueue[i];
                if (c.state == ChunkState.Queued) _chunks.Remove(c.coord);
                _buildQueue.RemoveAt(i);
            }
        }
    }

    /// <summary>Advance chunk builds within the frame budget.</summary>
    public void ProcessBuilds(float budgetMs, int chunksPerFrame, int maxConcurrentJobs)
    {
        _sw.Restart();

        // Activation of already-built chunks is cheap - flush those first.
        for (int i = _builtJobs.Count - 1; i >= 0; i--)
        {
            TerrainChunk c = _builtJobs[i];
            _builtJobs.RemoveAt(i);
            if (c.state == ChunkState.Built) Activate(c);
        }

        int started = 0;
        while (_buildQueue.Count > 0)
        {
            if (started >= Mathf.Max(1, chunksPerFrame)) break;
            if (started > 0 && _sw.Elapsed.TotalMilliseconds > budgetMs) break;
            if (_builtJobs.Count >= Mathf.Max(1, maxConcurrentJobs)) break;

            TerrainChunk c = PopNextBuild();
            if (c == null) break;

            BuildHeights(c);
            started++;

            // If there is budget left in this frame, activate immediately;
            // otherwise leave it for the start of the next frame.
            if (_sw.Elapsed.TotalMilliseconds <= budgetMs) Activate(c);
            else _builtJobs.Add(c);
        }
    }

    /// <summary>Release a few pending-unload chunks back to the pool each frame.</summary>
    public void ProcessUnloads(int maxPerFrame)
    {
        int done = 0;
        for (int i = _unloadQueue.Count - 1; i >= 0 && done < maxPerFrame; i--)
        {
            TerrainChunk c = _unloadQueue[i];
            _unloadQueue.RemoveAt(i);
            if (c.state != ChunkState.PendingUnload) continue; // revived meanwhile

            if (c.pooled != null)
            {
                _pool.Release(c.pooled);
                c.pooled = null;
                ActiveCount--;
            }
            _chunks.Remove(c.coord);
            done++;
        }
    }

    /// <summary>
    /// Guarantee the chunk under a position exists right now (synchronous).
    /// Used for the cell directly below each player so a plane can never reach
    /// ground that has no collider, regardless of budgets.
    /// </summary>
    public void EnsureChunkUnder(Vector3 worldPos)
    {
        ChunkCoord coord = CoordAt(worldPos);
        if (!_chunks.TryGetValue(coord, out TerrainChunk chunk))
        {
            chunk = new TerrainChunk { coord = coord, state = ChunkState.Queued, distance = 0f, priority = 0f };
            _chunks.Add(coord, chunk);
        }
        chunk.wantCollider = true;

        switch (chunk.state)
        {
            case ChunkState.Queued:
                BuildHeights(chunk);
                Activate(chunk);
                break;
            case ChunkState.Built:
                Activate(chunk);
                break;
            case ChunkState.PendingUnload:
                chunk.state = ChunkState.Active;
                break;
            case ChunkState.Active:
                if (chunk.pooled != null && !chunk.pooled.collider.enabled)
                    chunk.pooled.collider.enabled = true;
                break;
        }
    }

    /// <summary>Build everything currently queued, ignoring budgets (world init).</summary>
    public void ForceCompleteAll()
    {
        while (true)
        {
            TerrainChunk c = PopNextBuild();
            if (c == null) break;
            BuildHeights(c);
            Activate(c);
        }
        for (int i = _builtJobs.Count - 1; i >= 0; i--)
        {
            if (_builtJobs[i].state == ChunkState.Built) Activate(_builtJobs[i]);
        }
        _builtJobs.Clear();
    }

    public void Clear()
    {
        foreach (TerrainChunk c in _chunks.Values)
            if (c.pooled != null) _pool.Release(c.pooled);
        _chunks.Clear();
        _buildQueue.Clear();
        _builtJobs.Clear();
        _unloadQueue.Clear();
        ActiveCount = 0;
    }

    // ------------------------------------------------------------------ internals

    private TerrainChunk PopNextBuild()
    {
        while (_buildQueue.Count > 0)
        {
            // Queue is sorted ascending by priority; take from the front.
            TerrainChunk c = _buildQueue[0];
            _buildQueue.RemoveAt(0);
            if (c.state == ChunkState.Queued && _chunks.ContainsKey(c.coord)) return c;
        }
        return null;
    }

    private void CompactBuildQueue()
    {
        for (int i = _buildQueue.Count - 1; i >= 0; i--)
        {
            TerrainChunk c = _buildQueue[i];
            if (c.state != ChunkState.Queued || !_chunks.ContainsKey(c.coord))
                _buildQueue.RemoveAt(i);
        }
    }

    /// <summary>Expensive step: pool acquire + heights window upload (+ collider bake).</summary>
    private void BuildHeights(TerrainChunk chunk)
    {
        PooledChunk p = _pool.Acquire();
        chunk.pooled = p;
        _quality.ConfigureChunk(p, _visualsEnabled);

        Vector3 origin = chunk.OriginWorld(_chunkSize);
        p.go.transform.SetParent(_root, false);
        p.go.transform.localPosition = origin;

        int gx0 = chunk.coord.x * (_chunkRes - 1);
        int gz0 = chunk.coord.z * (_chunkRes - 1);
        for (int z = 0; z < _chunkRes; z++)
            for (int x = 0; x < _chunkRes; x++)
                _scratch[z, x] = _world.heights[gz0 + z, gx0 + x];

        // Collider enabled state decided before SetHeights so PhysX only bakes
        // the heightfield for chunks that actually need physics.
        p.collider.enabled = chunk.wantCollider;
        p.data.SetHeights(0, 0, _scratch);

        // Window of the global colormap this chunk shows.
        p.layer.tileOffset = new Vector2(origin.x, origin.z);

        chunk.state = ChunkState.Built;
    }

    /// <summary>Cheap step: switch the GameObject on and reset LOD so the next pass applies a band.</summary>
    private void Activate(TerrainChunk chunk)
    {
        if (chunk.pooled == null) return;
        chunk.pooled.go.SetActive(true);
        chunk.lodBand = -1;
        chunk.state = ChunkState.Active;
        ActiveCount++;
    }
}
