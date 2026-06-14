using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Distance-banded LOD for streamed terrain chunks. Near chunks render at full
/// heightmap detail with a low pixel error; mid and far bands raise the pixel
/// error, cap the heightmap LOD (heightmapMaximumLOD renders the same data at
/// half/quarter resolution without re-uploading anything) and switch to the
/// cheap pre-blended basemap, so distant terrain keeps its silhouette at a
/// fraction of the GPU/CPU cost. Band changes only touch a Terrain when the
/// band actually changed, so cruising inside one band costs nothing.
/// </summary>
[System.Serializable]
public class ChunkLODController
{
    [System.Serializable]
    public struct LodBand
    {
        [Tooltip("Band applies to chunks closer than this distance (meters). The last band should be very large (covers everything beyond the previous band).")]
        public float maxDistance;

        [Tooltip("Terrain pixel error inside this band. Higher = fewer triangles, visible simplification.")]
        public float pixelError;

        [Tooltip("Caps heightmap detail: 0 = full resolution, each step halves it (1 = 1/2, 2 = 1/4). Cheap geometric LOD without re-uploading heights.")]
        public int heightmapMaxLod;

        [Tooltip("Should chunks in this band cast shadows? Disable for far bands.")]
        public bool castShadows;
    }

    [Tooltip("LOD bands from near to far. Distances are from the chunk center to the nearest tracked player.")]
    public LodBand[] bands =
    {
        new LodBand { maxDistance = 2600f,   pixelError = 6f,  heightmapMaxLod = 0, castShadows = true },
        new LodBand { maxDistance = 6500f,   pixelError = 30f, heightmapMaxLod = 1, castShadows = false },
        new LodBand { maxDistance = 999999f, pixelError = 90f, heightmapMaxLod = 2, castShadows = false },
    };

    public int BandForDistance(float distance)
    {
        if (bands == null || bands.Length == 0) return 0;
        for (int i = 0; i < bands.Length; i++)
            if (distance <= bands[i].maxDistance) return i;
        return bands.Length - 1;
    }

    /// <summary>Apply band settings to every active chunk whose band changed.</summary>
    public void Apply(IEnumerable<TerrainChunk> chunks)
    {
        if (bands == null || bands.Length == 0) return;

        foreach (TerrainChunk chunk in chunks)
        {
            if (chunk.state != ChunkState.Active || chunk.pooled == null) continue;

            int band = BandForDistance(chunk.distance);
            if (band == chunk.lodBand) continue;
            chunk.lodBand = band;

            LodBand b = bands[band];
            Terrain t = chunk.pooled.terrain;
            t.heightmapPixelError = b.pixelError;
            t.heightmapMaximumLOD = Mathf.Max(0, b.heightmapMaxLod);
            t.shadowCastingMode = b.castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
