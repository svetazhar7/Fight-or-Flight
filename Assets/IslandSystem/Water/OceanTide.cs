using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Tides: slowly raises and lowers the whole ocean around its base sea level over a long period, so the
    /// shoreline visibly advances (high tide) and recedes (low tide). Poseidon animates the WAVES (fast, small)
    /// from the water body itself; this is the separate SLOW, large vertical swing on top. Put it on the Ocean
    /// root — the wave tiles are children, so they ride the tide, and the follower keeps its local Y at 0.
    /// </summary>
    [ExecuteAlways]
    public class OceanTide : MonoBehaviour
    {
        [Tooltip("Mean sea level (world Y) the tide oscillates around — the archipelago's waterLevel.")]
        public float seaLevel = 4f;
        [Tooltip("How far the water rises above / falls below sea level (m).")]
        public float amplitude = 1.5f;
        [Tooltip("Full low→high→low cycle length (seconds). Long = calm slow tide.")]
        public float period = 120f;

        void OnEnable() => Apply();

        void LateUpdate()
        {
            Apply();
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void Apply()
        {
            float t = Application.isPlaying ? Time.time : (float)UnityEditor_TimeSinceStartup();
            float y = seaLevel + amplitude * Mathf.Sin(t * (2f * Mathf.PI / Mathf.Max(1f, period)));
            var p = transform.position;
            if (!Mathf.Approximately(p.y, y)) transform.position = new Vector3(p.x, y, p.z);
        }

        static double UnityEditor_TimeSinceStartup()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.realtimeSinceStartupAsDouble;
#endif
        }
    }
}
