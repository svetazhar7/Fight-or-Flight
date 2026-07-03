using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// The ONE island-wide wind field. Grass, flowers and bushes all read the global phase uniforms
    /// (_IslandWindSpeed / _IslandWindScale) set here, so every plant sways TOGETHER — per-material speed
    /// values inevitably drifted out of phase and the foliage looked like it lived in a different wind.
    /// Applied once on load (editor + player); call <see cref="Apply"/> again after changing the values
    /// (e.g. if a weather system wants to gust).
    /// </summary>
    public static class IslandWind
    {
        static readonly int SpeedId = Shader.PropertyToID("_IslandWindSpeed");
        static readonly int ScaleId = Shader.PropertyToID("_IslandWindScale");

        /// <summary>Wind wave speed (phase time multiplier). One value for the whole world.</summary>
        public static float Speed = 1.1f;
        /// <summary>Spatial frequency of the wind waves (lower = broader gusts).</summary>
        public static float Scale = 0.15f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void RuntimeInit() => Apply();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorInit() => Apply();
#endif

        public static void Apply()
        {
            Shader.SetGlobalFloat(SpeedId, Speed);
            Shader.SetGlobalFloat(ScaleId, Scale);
        }
    }
}
