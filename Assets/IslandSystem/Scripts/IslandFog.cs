using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Our OWN distance fog — a thin driver over Unity's built-in `RenderSettings` fog, deliberately independent of
    /// COZY Weather (which we no longer use). The archipelago shaders (terrain, IslandSystem/Foliage, the tree
    /// impostors, water, grass) all `MixFog`, so this fogs the whole scene by distance for the far-away haze; pair
    /// it with a Depth Of Field volume for the "blur far" look. Values are re-applied every frame so nothing (e.g.
    /// a leftover COZY module) can quietly override them; tweak them live in the inspector.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class IslandFog : MonoBehaviour
    {
        [Tooltip("Master switch for the scene fog.")]
        public bool enableFog = true;

        [Tooltip("Fog / horizon colour — match it to the sky bottom so distant terrain dissolves seamlessly.")]
        public Color fogColor = new Color(0.68f, 0.76f, 0.84f, 1f);

        public FogMode mode = FogMode.ExponentialSquared;

        [Tooltip("Exponential fog thickness (used for Exponential / ExponentialSquared). ~0.0012 = gentle distance haze.")]
        public float density = 0.0012f;

        [Header("Linear mode only")]
        public float linearStart = 60f;
        public float linearEnd = 900f;

        void OnEnable() => Apply();
        void OnValidate() => Apply();
        void Update() => Apply();   // enforce every frame so nothing else can override our fog

        void Apply()
        {
            RenderSettings.fog = enableFog;
            if (!enableFog) return;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = mode;
            RenderSettings.fogDensity = density;
            RenderSettings.fogStartDistance = linearStart;
            RenderSettings.fogEndDistance = linearEnd;
        }
    }
}
