using UnityEngine;

/// <summary>
/// Gizmo visualization of the streaming system. Enable via
/// WorldStreamingManager.enableDebugVisualization and keep Gizmos on in the
/// Scene/Game view:
///   green   - active chunks (label shows LOD band)
///   yellow  - queued / building
///   red     - pending unload
///   cyan    - the chunk each tracked player is standing in
///   white circle  - load radius, grey circle - unload radius
/// A label above the first target shows live counters.
/// </summary>
[RequireComponent(typeof(WorldStreamingManager))]
public class StreamingDebugger : MonoBehaviour
{
    private WorldStreamingManager _mgr;

    private void OnDrawGizmos()
    {
        if (_mgr == null) _mgr = GetComponent<WorldStreamingManager>();
        if (_mgr == null || !_mgr.IsInitialized || !_mgr.enableDebugVisualization) return;

        ChunkManager cm = _mgr.ChunkManagerInstance;
        float size = cm.ChunkSize;
        WorldData world = _mgr.World;

        foreach (TerrainChunk c in cm.Chunks)
        {
            Vector3 center = c.CenterWorld(size);
            center.y = world.SampleHeightWorld(center.x, center.z) + 40f;

            switch (c.state)
            {
                case ChunkState.Active: Gizmos.color = new Color(0f, 1f, 0f, 0.7f); break;
                case ChunkState.PendingUnload: Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f); break;
                default: Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.9f); break;
            }
            Gizmos.DrawWireCube(center, new Vector3(size * 0.96f, 60f, size * 0.96f));
        }

        var targets = _mgr.tracker.Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].transform == null) continue;
            Vector3 pos = targets[i].transform.position;

            // Player's current chunk.
            ChunkCoord cc = cm.CoordAt(pos);
            Vector3 cellCenter = new Vector3((cc.x + 0.5f) * size, pos.y, (cc.z + 0.5f) * size);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(cellCenter, new Vector3(size, 80f, size));

            // Radii (altitude-scaled like the real config).
            float altitude = Mathf.Max(0f, pos.y - world.SampleHeightWorld(pos.x, pos.z));
            float mult = targets[i].isLocal
                ? Mathf.Clamp(_mgr.altitudeRadiusCurve.Evaluate(altitude), 1f, _mgr.maxRadiusMultiplier)
                : 1f;
            float loadR = (targets[i].isLocal ? _mgr.loadRadius : _mgr.remotePlayerRadius) * size * mult;
            float unloadR = Mathf.Max(_mgr.unloadRadius, _mgr.loadRadius + 1) * size * mult;

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(pos, loadR);
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            Gizmos.DrawWireSphere(pos, unloadR);

#if UNITY_EDITOR
            if (i == 0)
            {
                UnityEditor.Handles.Label(pos + Vector3.up * 120f,
                    $"chunks active {cm.ActiveCount} | queued {cm.QueuedCount} | unloading {cm.PendingUnloadCount}\n" +
                    $"pool free {_mgr.PoolInstance.FreeCount}/{_mgr.PoolInstance.CreatedCount} | grid {_mgr.ChunksPerAxis}x{_mgr.ChunksPerAxis} @ {size:F0} m\n" +
                    $"altitude {altitude:F0} m -> radius x{mult:F2}");
            }
#endif
        }
    }
}
