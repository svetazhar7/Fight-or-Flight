using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandSystem
{
    /// <summary>
    /// Fires COZY "Thunder And Lightning" strikes inside the storm-cloud ring at random intervals so the
    /// map-edge storm flickers with lightning (like COZY's own storm lightning). Lives on the "Storm Wall"
    /// object: each strike picks a random cloud segment and spawns a bolt there. Play mode only — the COZY
    /// strike prefab (`CozyThunder`) flashes a light, plays the bolt particles + thunder, then self-destructs.
    /// </summary>
    public class StormLightning : MonoBehaviour
    {
        [Tooltip("COZY 'Thunder And Lightning' prefab (one self-destructing strike). Auto-loaded if empty.")]
        public GameObject lightningPrefab;
        [Tooltip("Seconds between strikes (random within this range).")]
        public Vector2 intervalRange = new Vector2(3f, 8f);
        [Tooltip("World-Y range within the storm clouds where bolts appear.")]
        public Vector2 heightRange = new Vector2(120f, 850f);

        const string PrefabPath = "Packages/com.distantlands.cozy.core/Content/Prefabs/Thunder And Lightning.prefab";
        float nextStrike;

        void OnEnable()
        {
            ResolvePrefab();
            nextStrike = Time.time + Random.Range(intervalRange.x, intervalRange.y);
        }

        void Update()
        {
            if (!Application.isPlaying || lightningPrefab == null) return;
            if (Time.time < nextStrike) return;
            Strike();
            nextStrike = Time.time + Random.Range(intervalRange.x, intervalRange.y);
        }

        void Strike()
        {
            int n = transform.childCount;
            if (n == 0) return;
            Vector3 p = transform.GetChild(Random.Range(0, n)).position; // a point on the storm-cloud ring
            p.y = Random.Range(heightRange.x, heightRange.y);
            Instantiate(lightningPrefab, p, Quaternion.identity);
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
