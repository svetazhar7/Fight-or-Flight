using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandSystem
{
    /// <summary>
    /// Fires COZY "Thunder And Lightning" strikes in the storm-cloud ring — but ONLY while the local player
    /// is inside the storm band (i.e. has flown out to the deadly map edge). Lives on the "Storm Wall" object:
    /// each strike spawns a bolt on a nearby cloud segment so the lightning cracks AROUND the player. Play mode
    /// only — the COZY strike prefab (`CozyThunder`) flashes a light, plays the bolt particles + thunder, then
    /// self-destructs. Away from the storm the sky stays calm — no strikes.
    /// </summary>
    [RequireComponent(typeof(StormWall))]
    public class StormLightning : MonoBehaviour
    {
        [Tooltip("COZY 'Thunder And Lightning' prefab (one self-destructing strike). Auto-loaded if empty.")]
        public GameObject lightningPrefab;
        [Tooltip("Seconds between strikes (random within this range).")]
        public Vector2 intervalRange = new Vector2(3f, 8f);
        [Tooltip("World-Y range within the storm clouds where bolts appear.")]
        public Vector2 heightRange = new Vector2(120f, 850f);
        [Tooltip("Start striking this many metres BEFORE the player reaches the wall's inner face, so the " +
                 "storm warns you as you approach the edge.")]
        public float triggerInset = 250f;

        const string PrefabPath = "Packages/com.distantlands.cozy.core/Content/Prefabs/Thunder And Lightning.prefab";
        float nextStrike;
        StormWall wall;

        void OnEnable()
        {
            wall = GetComponent<StormWall>();
            ResolvePrefab();
            nextStrike = Time.time + Random.Range(intervalRange.x, intervalRange.y);
        }

        void Update()
        {
            if (!Application.isPlaying || lightningPrefab == null) return;

            // Only storm the edge while the player is actually in it — otherwise hold the timer so there's no
            // burst of queued strikes the instant they arrive.
            if (!PlayerInStorm())
            {
                nextStrike = Time.time + Random.Range(intervalRange.x, intervalRange.y);
                return;
            }

            if (Time.time < nextStrike) return;
            Strike();
            nextStrike = Time.time + Random.Range(intervalRange.x, intervalRange.y);
        }

        /// <summary>True when a local player exists AND is inside the storm band.</summary>
        bool PlayerInStorm()
        {
            if (wall == null) return false;
            var p = PlayerPosition();
            return p.HasValue && wall.IsInsideStorm(p.Value, triggerInset);
        }

        static System.Nullable<Vector3> PlayerPosition()
        {
            if (IslandGrassField.LocalViewer != null) return IslandGrassField.LocalViewer.position;
            var c = Camera.main;
            return c != null ? c.transform.position : (System.Nullable<Vector3>)null;
        }

        void Strike()
        {
            int n = transform.childCount;
            if (n == 0) return;

            // Prefer a cloud segment NEAR the player so the bolt actually cracks in view, not on the far side
            // of the huge ring. Find the nearest segment, then jitter a couple of neighbours along the ring.
            var pp = PlayerPosition();
            int idx = pp.HasValue ? NearestSegment(pp.Value) : Random.Range(0, n);
            idx = ((idx + Random.Range(-2, 3)) % n + n) % n;

            Vector3 p = transform.GetChild(idx).position; // a point on the storm-cloud ring
            p.y = Random.Range(heightRange.x, heightRange.y);
            Instantiate(lightningPrefab, p, Quaternion.identity);
        }

        int NearestSegment(Vector3 worldPos)
        {
            int best = 0; float bestSq = float.MaxValue;
            for (int i = 0; i < transform.childCount; i++)
            {
                Vector3 d = transform.GetChild(i).position - worldPos;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        void ResolvePrefab()
        {
            if (lightningPrefab != null) return;
#if UNITY_EDITOR
            lightningPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#endif
        }
    }
}
