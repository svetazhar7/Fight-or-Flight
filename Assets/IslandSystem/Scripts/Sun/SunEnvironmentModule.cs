using UnityEngine;
using UnityEngine.Rendering;

namespace IslandSystem.Sun
{
    /// <summary>
    /// Couples the sun to the rest of the scene: ambient light, reflections, fog and the sky's horizon glow / haze.
    /// The BASE values come from StylizedSkyController (when referenced by the SunSystem) or from the local base
    /// fields below; this module then modulates them by sun colour and elevation, so re-applying is always
    /// idempotent — nothing compounds. Near the horizon the fog warms toward the sun, the horizon starts to glow and
    /// a light haze pools around the sun's side of the sky.
    /// </summary>
    [DisallowMultipleComponent]
    public class SunEnvironmentModule : SunModuleBase
    {
        public override int Order => 30;

        [Header("Ambient")]
        [Range(0f, 1f), Tooltip("How much the sun colour tints the ambient light.")]
        public float ambientInfluence = 0.45f;
        [Range(0f, 2f), Tooltip("Ambient brightness that comes FROM the sun (scaled by sun intensity).")]
        public float ambientFromSun = 0.35f;

        [Header("Reflections")]
        [Range(0f, 1f), Tooltip("RenderSettings.reflectionIntensity — how strongly the environment reflects.")]
        public float reflectionIntensity = 1f;
        [Range(0f, 1f), Tooltip("Dim reflections as the sun sets so water/gloss doesn't stay noon-bright at dusk.")]
        public float reflectionSunset = 0.35f;

        [Header("Fog coupling")]
        [Range(0f, 1f), Tooltip("Lerp of the fog colour toward the sun colour when the sun is low (air catches the light).")]
        public float fogTintInfluence = 0.5f;
        [Range(0f, 2f), Tooltip("Fog density boost near the horizon — thicker, warmer air at sunset.")]
        public float fogDensityBoost = 0.6f;
        [Range(0f, 1f), Tooltip("Light absorption: darkens the fog colour slightly so thick air loses energy.")]
        public float lightAbsorption = 0.12f;

        [Header("Sky atmosphere (skybox shader)")]
        [Range(0f, 2f), Tooltip("Glow band hugging the horizon, tinted by the sun.")]
        public float horizonGlow = 0.7f;
        [Range(0.02f, 0.6f), Tooltip("Vertical height of the horizon glow band.")]
        public float horizonGlowHeight = 0.18f;
        [Range(0f, 2f), Tooltip("Haze veil density mixed over the lower sky.")]
        public float hazeDensity = 0.55f;
        [Range(0.05f, 0.9f), Tooltip("How high the haze climbs from the horizon.")]
        public float hazeHeight = 0.3f;
        [Tooltip("Atmospheric tint of the haze (multiplied with the sun/fog colours).")]
        public Color atmosphericTint = new Color(1f, 0.94f, 0.86f);
        [Range(0f, 2f), Tooltip("Air scattering: widens the bright region of sky around the sun's side.")]
        public float airScattering = 1f;

        [Header("Base values (used only when SunSystem has no StylizedSkyController reference)")]
        public Color baseAmbientColor = new Color(1f, 0.95f, 0.90f);
        [Range(0f, 2f)] public float baseAmbientIntensity = 0.55f;
        public Color baseFogColor = new Color(1f, 0.92f, 0.80f);
        [Min(0f)] public float baseFogDensity = 0.00025f;

        [Header("Scene")]
        [Range(0.25f, 2f), Tooltip("Overall scene brightness multiplier (ambient AND sun together).")]
        public float sceneBrightness = 1f;

        static readonly int ID_HorizonGlowColor = Shader.PropertyToID("_HorizonGlowColor");
        static readonly int ID_HorizonGlowParams = Shader.PropertyToID("_HorizonGlowParams"); // x strength, y height
        static readonly int ID_HazeColor = Shader.PropertyToID("_HazeColor");
        static readonly int ID_HazeParams = Shader.PropertyToID("_HazeParams");               // x density, y height, z airScattering

        public override void Apply(in SunContext ctx)
        {
            var system = GetComponent<SunSystem>();
            var sky = system != null ? system.skyController : null;

            // ---- Base values (sky controller wins when present) ----
            Color ambBase = sky != null ? sky.ambientColor * sky.ambientIntensity : baseAmbientColor * baseAmbientIntensity;
            Color fogBase = sky != null ? sky.fogColor : baseFogColor;
            float fogDenBase = sky != null ? sky.fogDensity * (1f + sky.horizonHaze) : baseFogDensity;

            // ---- Ambient: tint toward the sun and add the sun's own bounce contribution ----
            Color amb = Color.Lerp(ambBase, ambBase * ctx.sunColor, ambientInfluence);
            amb += ctx.sunColor * (ctx.sunIntensity * ambientFromSun * 0.25f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = amb * sceneBrightness;

            // ---- Reflections ----
            RenderSettings.reflectionIntensity = reflectionIntensity * Mathf.Lerp(1f, 1f - reflectionSunset, ctx.horizonFactor);

            // ---- Fog ----
            float sunLow = ctx.horizonFactor;
            Color fog = Color.Lerp(fogBase, ctx.sunColor * atmosphericTint, sunLow * fogTintInfluence);
            fog *= 1f - lightAbsorption * ctx.atmosphericDensity * 0.5f;
            RenderSettings.fogColor = fog * sceneBrightness;
            RenderSettings.fogDensity = fogDenBase * (1f + sunLow * fogDensityBoost * ctx.atmosphericDensity);

            // ---- Skybox horizon glow + haze ----
            var mat = ctx.skyboxMaterial;
            if (mat != null)
            {
                Color glowC = ctx.sunColor * atmosphericTint;
                float strength = horizonGlow * (0.35f + 0.65f * ctx.haloBoost) * ctx.atmosphericDensity;
                mat.SetColor(ID_HorizonGlowColor, glowC);
                mat.SetVector(ID_HorizonGlowParams, new Vector4(strength, horizonGlowHeight, 0f, 0f));
                mat.SetColor(ID_HazeColor, fog);
                mat.SetVector(ID_HazeParams, new Vector4(
                    hazeDensity * ctx.atmosphericDensity * (0.4f + 0.6f * sunLow),
                    hazeHeight,
                    airScattering, 0f));
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
            }

            // ---- Scene brightness onto the light itself ----
            if (ctx.light != null) ctx.light.intensity = ctx.sunIntensity * sceneBrightness;
        }
    }
}
