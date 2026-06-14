using System.Collections.Generic;
using UnityEngine;

/// <summary>One reusable set of Unity objects backing a terrain chunk.</summary>
public class PooledChunk
{
    public GameObject go;
    public Terrain terrain;
    public TerrainCollider collider;
    public TerrainData data;
    public TerrainLayer layer;
}

/// <summary>
/// Pool of pre-configured Terrain GameObjects. Creating a TerrainData (and the
/// alphamap that goes with it) is expensive and allocates, so released chunks
/// are deactivated and reused instead of destroyed. All pooled TerrainData
/// share the same heightmap resolution and size, so a reused chunk only needs
/// a SetHeights call and a layer offset to represent any cell of the map.
/// </summary>
public class ChunkPool
{
    private readonly Stack<PooledChunk> _free = new();
    private Transform _parent;
    private WorldData _world;
    private int _chunkRes;
    private float _chunkSize;
    private bool _poolingEnabled;
    private int _maxPooled;
    private int _created;

    public int FreeCount => _free.Count;
    public int CreatedCount => _created;

    public void Initialize(Transform parent, WorldData world, int chunkRes, float chunkSize,
        bool poolingEnabled, int maxPooled)
    {
        _parent = parent;
        _world = world;
        _chunkRes = chunkRes;
        _chunkSize = chunkSize;
        _poolingEnabled = poolingEnabled;
        _maxPooled = Mathf.Max(1, maxPooled);
    }

    public PooledChunk Acquire()
    {
        while (_free.Count > 0)
        {
            PooledChunk p = _free.Pop();
            if (p != null && p.go != null) return p;
        }
        return CreateNew();
    }

    public void Release(PooledChunk p)
    {
        if (p == null || p.go == null) return;
        p.go.SetActive(false);
        if (_poolingEnabled && _free.Count < _maxPooled)
        {
            _free.Push(p);
        }
        else
        {
            DestroyPooled(p);
            _created--;
        }
    }

    public void Clear()
    {
        while (_free.Count > 0) DestroyPooled(_free.Pop());
        _created = 0;
    }

    private PooledChunk CreateNew()
    {
        var p = new PooledChunk();
        p.go = new GameObject("Chunk");
        p.go.transform.SetParent(_parent, false);
        p.go.SetActive(false);

        p.data = new TerrainData();
        p.data.heightmapResolution = _chunkRes;
        p.data.size = new Vector3(_chunkSize, _world.worldSize.y, _chunkSize);

        // One layer showing this chunk's window of the global colormap. The
        // window is selected per-assignment via tileOffset; alpha stays 0 so
        // URP TerrainLit reads zero smoothness (matte ground).
        p.layer = new TerrainLayer
        {
            diffuseTexture = _world.colormap,
            tileSize = new Vector2(_world.worldSize.x, _world.worldSize.z),
            tileOffset = Vector2.zero,
            smoothness = 0f,
            metallic = 0f,
            specular = Color.black
        };
        p.data.terrainLayers = new[] { p.layer };

        // Tiny all-ones alphamap: a single layer fully painted.
        p.data.alphamapResolution = 16;
        var alphas = new float[16, 16, 1];
        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
                alphas[z, x, 0] = 1f;
        p.data.SetAlphamaps(0, 0, alphas);
        p.data.baseMapResolution = 64;

        p.terrain = p.go.AddComponent<Terrain>();
        p.terrain.terrainData = p.data;
        p.collider = p.go.AddComponent<TerrainCollider>();
        p.collider.terrainData = p.data;

        _created++;
        return p;
    }

    private static void DestroyPooled(PooledChunk p)
    {
        if (p == null || p.go == null) return;
        if (Application.isPlaying)
        {
            Object.Destroy(p.data);
            Object.Destroy(p.layer);
            Object.Destroy(p.go);
        }
        else
        {
            Object.DestroyImmediate(p.go);
            Object.DestroyImmediate(p.data);
            Object.DestroyImmediate(p.layer);
        }
    }
}
