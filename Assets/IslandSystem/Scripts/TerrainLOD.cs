using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Per-island terrain LOD on top of Unity Terrain's own within-terrain continuous LOD. As an island gets
    /// farther from the viewer it ramps up <see cref="Terrain.heightmapPixelError"/> (so its patches collapse
    /// to far fewer triangles) and pulls <see cref="Terrain.basemapDistance"/> in (so it renders with the cheap
    /// pre-baked basemap texture instead of the full multi-layer splatmap shader). A close island you're flying
    /// over stays crisp; the ring of distant islands costs a fraction. Distance is measured to the island's
    /// bounding box, so big islands don't over-simplify just because their pivot is far.
    /// Values are only written when they change past a threshold, so the terrain isn't re-dirtied every frame.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Terrain))]
    public class TerrainLOD : MonoBehaviour
    {
        [Tooltip("Full detail within this distance to the island (m).")]
        public float nearDistance = 300f;
        [Tooltip("Maximum simplification at/after this distance (m).")]
        public float farDistance = 3000f;

        [Header("Heightmap pixel error (higher = fewer triangles)")]
        public float nearPixelError = 5f;
        public float farPixelError = 60f;

        [Header("Basemap — WHOLE-island decision (never cuts an island in half)")]
        [Tooltip("Beyond this distance to the island, the WHOLE island drops to the cheap basemap texture. " +
                 "Within it, the whole island uses the detailed splatmap. Never a mid-island seam.")]
        public float basemapSwitch = 800f;

        Terrain _t;
        float _lastError = -1f, _lastBasemap = -1f;

        void OnEnable() { _t = GetComponent<Terrain>(); Tick(); }

        void LateUpdate() { Tick(); }

        void Tick()
        {
            if (_t == null) { _t = GetComponent<Terrain>(); if (_t == null) return; }
            if (_t.terrainData == null) return;

            // World-space distance from the viewer to the island's bounding box (0 if inside).
            Bounds b = _t.terrainData.bounds;
            b.center += transform.position;
            Vector3 vp = ViewerPos();
            float d = Mathf.Sqrt(b.SqrDistance(vp));
            float span = b.size.magnitude;   // near-edge → far-edge distance across the island

            // Geometry LOD: continuous, seamless (Unity stitches the patch quadtree) — safe to ramp per island.
            float k = Mathf.Clamp01((d - nearDistance) / Mathf.Max(1f, farDistance - nearDistance));
            float err = Mathf.Lerp(nearPixelError, farPixelError, k);

            // Basemap: a WHOLE-island choice so the splatmap→basemap cutoff never lands mid-island (that was
            // the visible "cut"). Close → cover the entire island (all splatmap); far → 0 (all basemap, and the
            // island is small on screen so the res drop is invisible + consistent).
            float bm = (d < basemapSwitch) ? (d + span + 100f) : 0f;

            if (Mathf.Abs(err - _lastError) > 0.5f) { _t.heightmapPixelError = err; _lastError = err; }
            if (Mathf.Abs(bm - _lastBasemap) > 5f) { _t.basemapDistance = bm; _lastBasemap = bm; }

#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        Vector3 ViewerPos()
        {
            if (Application.isPlaying && IslandGrassField.LocalViewer != null) return IslandGrassField.LocalViewer.position;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null) return sv.camera.transform.position;
            }
#endif
            var c = Camera.main;
            return c != null ? c.transform.position : transform.position;
        }
    }
}
