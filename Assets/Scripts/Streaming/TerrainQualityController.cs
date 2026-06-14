using UnityEngine;

/// <summary>
/// Applies global terrain quality settings to streamed chunks and to the local
/// camera. Chunks get instanced rendering and a shared grouping id (so Unity
/// auto-stitches neighbouring terrains and hides LOD seams); on a dedicated
/// server all visual drawing is turned off while colliders keep working. Also
/// makes sure the local camera can actually see to the horizon - a short far
/// clip plane is the classic cause of a visible "end of the world".
/// </summary>
[System.Serializable]
public class TerrainQualityController
{
    private const int TerrainGroupId = 71;

    [Tooltip("Minimum far clip plane enforced on the local player's camera so the far terrain ring is visible. 0 = don't touch cameras.")]
    public float minCameraFarClip = 30000f;

    [Tooltip("Vertices per axis of the single low-poly horizon mesh that covers the whole map beyond chunk range.")]
    [Range(33, 257)] public int farMeshResolution = 129;

    [Tooltip("How far (m) the horizon mesh is sunk below the true terrain so real chunks always render on top of it.")]
    public float farMeshSinkMeters = 4f;

    /// <summary>One-time static configuration of a freshly created pooled chunk.</summary>
    public void ConfigureChunk(PooledChunk p, bool visualsEnabled)
    {
        Terrain t = p.terrain;

        // Terrains created via AddComponent have no material template; under
        // URP that renders flat grey. Use the pipeline's TerrainLit material.
        if (t.materialTemplate == null)
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (rp != null && rp.defaultTerrainMaterial != null)
                t.materialTemplate = rp.defaultTerrainMaterial;
        }

        t.drawInstanced = true;
        t.groupingID = TerrainGroupId;
        t.allowAutoConnect = true;       // Unity stitches edges of neighbouring chunks
        t.drawTreesAndFoliage = false;
        t.heightmapPixelError = 6f;

        // Never fall back to the baked basemap: with a single layer showing a
        // window of the global colormap the full material is just one texture
        // sample, while the basemap bakes our 20 km tile into uniform mud.
        t.basemapDistance = 100000f;

        if (!visualsEnabled)
        {
            // Dedicated server: collider only, never draw.
            t.drawHeightmap = false;
            t.enabled = true; // Terrain component must stay enabled for auto-connect; drawing is off.
        }
    }

    /// <summary>Raise the local camera's far plane if it cannot see the horizon.</summary>
    public void EnsureCameraFarClip()
    {
        if (minCameraFarClip <= 0f) return;
        Camera cam = Camera.main;
        if (cam == null)
        {
            // Player camera may not carry the MainCamera tag - fix any active one.
            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
                if (cams[i].farClipPlane < minCameraFarClip)
                    cams[i].farClipPlane = minCameraFarClip;
            return;
        }
        if (cam.farClipPlane < minCameraFarClip)
            cam.farClipPlane = minCameraFarClip;
    }
}
