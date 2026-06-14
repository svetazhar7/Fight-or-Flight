using UnityEngine;

/// <summary>Integer grid coordinate of a terrain chunk.</summary>
public struct ChunkCoord : System.IEquatable<ChunkCoord>
{
    public int x;
    public int z;

    public ChunkCoord(int x, int z) { this.x = x; this.z = z; }

    public bool Equals(ChunkCoord other) => x == other.x && z == other.z;
    public override bool Equals(object obj) => obj is ChunkCoord c && Equals(c);
    public override int GetHashCode() => x * 73856093 ^ z * 19349663;
    public override string ToString() => $"({x},{z})";
}

public enum ChunkState
{
    Queued,         // wanted, waiting for build budget
    Built,          // heights uploaded, awaiting activation
    Active,         // visible / collidable in the world
    PendingUnload   // scheduled for release back to the pool
}

/// <summary>
/// Runtime record of one streamed terrain chunk: where it is, what pooled
/// Unity objects back it, and where it is in the build pipeline.
/// </summary>
public class TerrainChunk
{
    public ChunkCoord coord;
    public ChunkState state;
    public PooledChunk pooled;       // null until acquired from the pool

    public float distance;           // meters from the nearest tracked target (this tick)
    public float priority;           // sort key for the build queue (distance + preload penalty)
    public bool isPreload;           // queued by the preload ring, not the core radius
    public bool wantCollider;        // within collider radius of some target
    public int lodBand = -1;         // last applied LOD band (-1 = none yet)

    public Vector3 OriginWorld(float chunkSizeMeters)
        => new Vector3(coord.x * chunkSizeMeters, 0f, coord.z * chunkSizeMeters);

    public Vector3 CenterWorld(float chunkSizeMeters)
        => new Vector3((coord.x + 0.5f) * chunkSizeMeters, 0f, (coord.z + 0.5f) * chunkSizeMeters);
}
