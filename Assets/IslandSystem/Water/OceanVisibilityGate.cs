using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Stops the ocean from rendering — and, crucially, from running its expensive per-frame planar-REFLECTION
    /// pass (a full mirrored scene re-render on the LOD0 ring) — when the viewer is down INSIDE an island with no
    /// sea in view. The water tiles themselves are opaque + depth-tested, so tiles hidden behind terrain already
    /// early-Z out for free; the reflection camera does NOT — it re-renders the whole mirrored world every frame
    /// regardless. This gate hides the LOD rings (and with them the reflection) when the viewer is BOTH surrounded
    /// by land within <see cref="checkRadius"/> AND close to the ground (so terrain occludes the surrounding sea),
    /// and re-shows them the instant a probe finds sea or the viewer climbs. Hysteresis avoids coastline flicker.
    /// The ocean's gameplay collider + tide live on the Ocean ROOT (kept active), so water queries still work.
    /// </summary>
    [ExecuteAlways]
    public class OceanVisibilityGate : MonoBehaviour
    {
        [Tooltip("Water surface height — terrain probed above this counts as land.")]
        public float waterLevel = 4f;
        [Tooltip("The viewer must be surrounded by land within this radius (all probe directions) to hide the ocean.")]
        public float checkRadius = 250f;
        [Tooltip("Terrain must rise at least this far ABOVE the water to count as 'land' at a probe point.")]
        public float landMargin = 1.5f;
        [Tooltip("Only hide when the viewer is within this height above the local ground (higher up the sea is visible).")]
        public float maxHideHeight = 55f;
        [Tooltip("Seconds a new visible/hidden state must hold before it is applied (anti-flicker).")]
        public float switchDelay = 0.4f;
        [Tooltip("How often (s) to re-probe the surroundings.")]
        public float probeInterval = 0.15f;

        [Tooltip("The LOD ring GameObjects to toggle (LOD0 also carries the planar reflection).")]
        public Transform[] rings;

        static readonly Vector2[] Dirs =
        {
            new Vector2(0, 1), new Vector2(0.7071f, 0.7071f), new Vector2(1, 0), new Vector2(0.7071f, -0.7071f),
            new Vector2(0, -1), new Vector2(-0.7071f, -0.7071f), new Vector2(-1, 0), new Vector2(-0.7071f, 0.7071f)
        };
        static readonly RaycastHit[] _hits = new RaycastHit[8];

        bool _visible = true;
        bool _pending = true;
        float _pendingSince;
        float _nextProbe;

        void OnEnable() { SetVisible(true, force: true); _nextProbe = 0f; }

        void Update()
        {
            if (Time.realtimeSinceStartup < _nextProbe) return;
            _nextProbe = Time.realtimeSinceStartup + Mathf.Max(0.02f, probeInterval);

            Vector3 v = ViewerPos();
            bool wantVisible = SeaVisibleNear(v);

            // Debounce: a candidate state must persist for switchDelay before it takes effect.
            if (wantVisible != _pending) { _pending = wantVisible; _pendingSince = Time.realtimeSinceStartup; }
            if (_pending != _visible && Time.realtimeSinceStartup - _pendingSince >= switchDelay)
                SetVisible(_pending, force: false);
        }

        /// <summary>True if the ocean should render: sea found near the viewer, OR the viewer is high enough that
        /// the surrounding sea would be visible over the terrain anyway.</summary>
        bool SeaVisibleNear(Vector3 v)
        {
            float ground;
            bool centerLand = SampleLand(v, out ground);

            // High above the local ground → the sea around the island is on-screen; always show.
            if (v.y - ground > maxHideHeight) return true;

            // Any probe direction that is NOT land (open sea, or underwater terrain) → sea is near → show.
            for (int i = 0; i < Dirs.Length; i++)
            {
                Vector3 p = v + new Vector3(Dirs[i].x, 0f, Dirs[i].y) * checkRadius;
                float g;
                if (!SampleLand(p, out g)) return true;
            }
            // Center + every direction is land, and the viewer is low → sea occluded → hide.
            return false;
        }

        /// <summary>Probe terrain straight down at <paramref name="xz"/>. Returns true if solid land rises above the
        /// water there; <paramref name="groundY"/> is the terrain height (waterLevel if none/underwater).</summary>
        bool SampleLand(Vector3 xz, out float groundY)
        {
            groundY = waterLevel;
            Vector3 origin = new Vector3(xz.x, 4000f, xz.z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, 8000f, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                // Skip our own ocean colliders (the flat surface box / water tiles) — we only want the seabed/land.
                if (_hits[i].collider != null && _hits[i].collider.transform.IsChildOf(transform)) continue;
                if (_hits[i].collider is TerrainCollider || _hits[i].collider is MeshCollider || _hits[i].collider is BoxCollider)
                {
                    if (_hits[i].point.y > best) best = _hits[i].point.y;
                    found = true;
                }
            }
            if (!found) return false;              // open sea (no terrain below)
            groundY = best;
            return best > waterLevel + landMargin; // land only if it rises clearly above the water
        }

        void SetVisible(bool visible, bool force)
        {
            if (!force && visible == _visible) return;
            _visible = visible;
            _pending = visible;
            if (rings == null) return;
            foreach (var r in rings)
                if (r != null && r.gameObject.activeSelf != visible) r.gameObject.SetActive(visible);
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
