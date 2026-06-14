using UnityEngine;

/// <summary>
/// Pure data product of procedural generation: the global heightmap, the
/// biome-tinted colormap texture and the biome map - with no Unity Terrain
/// objects attached. Generated once per seed and identical on every networked
/// peer (the seed is the only thing FishNet syncs), so the chunk streaming
/// system can build, recycle and discard visual terrain freely on each client
/// without touching the network.
/// </summary>
public class WorldData
{
    public int seed;
    public float[,] heights;     // [z, x], normalized 0..1
    public int heightRes;        // samples per axis ((2^n)+1, e.g. 2049)
    public Vector3 worldSize;    // x/z = map size in meters, y = max terrain height
    public Texture2D colormap;   // biome-tinted albedo for the whole map (alpha = smoothness, kept 0)
    public BiomeMap biomeMap;

    /// <summary>Bilinear height sample in normalized map coords (0..1), result 0..1.</summary>
    public float SampleHeight01(float nx, float nz)
    {
        if (heights == null) return 0f;
        float fx = Mathf.Clamp01(nx) * (heightRes - 1);
        float fz = Mathf.Clamp01(nz) * (heightRes - 1);
        int x0 = Mathf.Min((int)fx, heightRes - 2);
        int z0 = Mathf.Min((int)fz, heightRes - 2);
        float tx = fx - x0;
        float tz = fz - z0;
        float h00 = heights[z0, x0];
        float h10 = heights[z0, x0 + 1];
        float h01 = heights[z0 + 1, x0];
        float h11 = heights[z0 + 1, x0 + 1];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    /// <summary>Terrain height in world meters at a world-space XZ position.</summary>
    public float SampleHeightWorld(float worldX, float worldZ)
    {
        return SampleHeight01(worldX / worldSize.x, worldZ / worldSize.z) * worldSize.y;
    }

    /// <summary>Approximate slope in degrees at normalized map coords (central differences).</summary>
    public float SampleSteepnessDeg(float nx, float nz)
    {
        float d = 1f / (heightRes - 1);
        float cellX = worldSize.x * d;
        float cellZ = worldSize.z * d;
        float dhdx = (SampleHeight01(nx + d, nz) - SampleHeight01(nx - d, nz)) * worldSize.y / (2f * cellX);
        float dhdz = (SampleHeight01(nx, nz + d) - SampleHeight01(nx, nz - d)) * worldSize.y / (2f * cellZ);
        return Mathf.Atan(Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz)) * Mathf.Rad2Deg;
    }
}
