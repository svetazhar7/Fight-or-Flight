using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace IslandSystem.Sun
{
    /// <summary>
    /// Orchestrator of the modular sun stack. Owns what every module needs exactly once: the sun's position on the
    /// sky sphere (elevation/azimuth or a slow auto-orbit), the graded light colour (Kelvin temperature + tint +
    /// elevation gradient) and the atmosphere response — the "sunset engine" that automatically warms, dims and
    /// thickens everything as the sun drops towards the horizon. Each visual concern (disc, halo, god rays, shadows,
    /// environment coupling) lives in its own SunModuleBase component on this GameObject and can be toggled
    /// independently. Everything applies instantly from OnValidate/OnEnable in both Scene and Game view; the only
    /// per-frame work is the optional auto-orbit in play mode.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SunSystem : MonoBehaviour
    {
        [Header("References")]
        public Light sunLight;
        public Material skyboxMaterial;
        [Tooltip("Optional: the sky/grading controller. Its ambient & fog values become the BASE that the sun environment module modulates.")]
        public StylizedSkyController skyController;

        [Header("Position on the sky sphere")]
        [Range(-15f, 90f), Tooltip("Elevation above the horizon (deg). Everything sunset-related keys off this.")]
        public float elevation = 32f;
        [Range(0f, 360f), Tooltip("Compass direction of the sun.")]
        public float azimuth = 155f;

        [Header("Motion (optional — leave off for full manual control)")]
        [Tooltip("Slowly orbit the sun in play mode. Manual elevation/azimuth stay authoritative in edit mode.")]
        public bool autoOrbit = false;
        [Tooltip("Elevation degrees per second while orbiting (use tiny values, e.g. 0.25).")]
        public float orbitSpeed = 0.25f;

        [Header("Light")]
        [Range(0f, 3f)] public float intensity = 1.15f;
        [Range(1500f, 12000f), Tooltip("Base colour temperature of the sun in Kelvin. ~5600 = warm white daylight.")]
        public float colorTemperature = 5600f;
        [Range(0f, 1f), Tooltip("How much the Kelvin colour drives the light (0 = pure tint colour).")]
        public float temperatureBlend = 0.65f;
        [Tooltip("Artist tint multiplied over the temperature colour.")]
        public Color tint = new Color(1f, 0.97f, 0.92f);

        [Header("Atmosphere response — automatic near the horizon")]
        [Tooltip("Sun colour over elevation: left = at the horizon, right = high noon. Applied on top of temperature+tint.")]
        public Gradient colorByElevation;
        [Tooltip("Light intensity multiplier over elevation (left = horizon).")]
        public AnimationCurve intensityByElevation;
        [Tooltip("God-ray master visibility over elevation (left = horizon). Rays read best with a low sun.")]
        public AnimationCurve rayVisibilityByElevation;
        [Tooltip("Halo / atmospheric glow boost over elevation (left = horizon).")]
        public AnimationCurve haloBoostByElevation;
        [Range(0f, 1f), Tooltip("Extra warm push of the light colour right at the horizon.")]
        public float horizonWarmth = 0.55f;
        [Range(0f, 1f), Tooltip("Slight desaturation of the light at high noon keeps midday soft instead of yellow.")]
        public float noonDesaturate = 0.1f;

        [Header("Atmosphere thickness")]
        [Range(0f, 3f), Tooltip("Global air density dial — scales haze, fog boosts and scattering responses in all modules.")]
        public float atmosphericDensity = 1f;

        [SerializeField, HideInInspector] bool _defaultsSet;

        static readonly int ID_SkySunDir = Shader.PropertyToID("_SkySunDir");

        readonly List<SunModuleBase> _modules = new List<SunModuleBase>();

        void OnEnable() { EnsureDefaults(false); ApplyAll(); }
        void OnValidate() { if (isActiveAndEnabled) { EnsureDefaults(false); ApplyAll(); } }

        void Update()
        {
            if (!autoOrbit || !Application.isPlaying) return;
            elevation += orbitSpeed * Time.deltaTime;
            if (elevation > 90f) elevation = -15f;
            ApplyAll();
        }

        [ContextMenu("Reapply")]
        public void ApplyAll()
        {
            var ctx = BuildContext();

            if (sunLight != null)
            {
                // The sun is a DISTANT source: force Directional so every shadow is parallel (orthographic cascades)
                // and lighting is uniform across the island — never the diverging shadows / falloff of a point light.
                if (sunLight.type != LightType.Directional) sunLight.type = LightType.Directional;
                sunLight.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
                sunLight.color = ctx.sunColor;
                sunLight.intensity = ctx.sunIntensity;
                sunLight.enabled = ctx.sunIntensity > 0.001f;
                RenderSettings.sun = sunLight;
            }
            Shader.SetGlobalVector(ID_SkySunDir, new Vector4(ctx.toSun.x, ctx.toSun.y, ctx.toSun.z, 0f));

            GetComponents(_modules);
            foreach (var m in _modules.OrderBy(m => m.Order))
                if (m.enabled) m.Apply(in ctx);
        }

        SunContext BuildContext()
        {
            float el01 = Mathf.Clamp01(elevation / 60f);
            float horizon = 1f - Mathf.Clamp01(elevation / 25f);

            // Cloud coupling: the cloud-shadow module (when present) tracks how much the clouds cover the sun.
            var cloudMod = GetComponent<SunCloudShadowModule>();
            bool clouded = cloudMod != null && cloudMod.isActiveAndEnabled;

            Color c = Color.Lerp(tint, SunColorUtil.KelvinToColor(colorTemperature) * tint, temperatureBlend);
            if (colorByElevation != null) c *= colorByElevation.Evaluate(el01);
            c = SunColorUtil.ShiftTemperature(c, horizon * horizonWarmth);
            c = SunColorUtil.AdjustSaturation(c, 1f - (1f - horizon) * noonDesaturate);
            c.a = 1f;

            float i = intensity * (intensityByElevation != null ? Mathf.Max(0f, intensityByElevation.Evaluate(el01)) : 1f);
            if (elevation < 0f) i *= Mathf.Clamp01(1f + elevation / 8f);   // dip below horizon fades the light out
            if (clouded) i *= cloudMod.DiffuseFade;                        // clouds soak up direct light

            var rot = Quaternion.Euler(elevation, azimuth, 0f);
            return new SunContext
            {
                light = sunLight,
                skyboxMaterial = skyboxMaterial,
                toSun = -(rot * Vector3.forward),
                elevationDeg = elevation,
                elevation01 = el01,
                horizonFactor = horizon,
                sunColor = c,
                sunIntensity = i,
                rayVisibility = rayVisibilityByElevation != null ? Mathf.Clamp01(rayVisibilityByElevation.Evaluate(el01)) : 1f,
                haloBoost = haloBoostByElevation != null ? Mathf.Clamp01(haloBoostByElevation.Evaluate(el01)) : 0f,
                atmosphericDensity = atmosphericDensity,
                cloudSunVisibility = clouded ? cloudMod.SunVisibility : 1f,
                cloudDiffuseFade = clouded ? cloudMod.DiffuseFade : 1f,
                cloudGlowFade = clouded ? cloudMod.GlowFade : 1f,
                cloudRayFade = clouded ? cloudMod.RayFade : 1f,
                cloudShadowFade = clouded ? cloudMod.ShadowFade : 1f,
                cloudAmbientBoost = clouded ? cloudMod.AmbientBoost : 0f
            };
        }

        [ContextMenu("Reset Atmosphere Curves To Defaults")]
        public void ResetDefaults() { EnsureDefaults(true); ApplyAll(); }

        void EnsureDefaults(bool force)
        {
            if (_defaultsSet && !force) return;
            colorByElevation = new Gradient();
            colorByElevation.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.62f, 0.36f), 0f),     // horizon: deep warm orange
                    new GradientColorKey(new Color(1f, 0.82f, 0.62f), 0.25f),  // low sun: golden
                    new GradientColorKey(new Color(1f, 0.96f, 0.90f), 0.6f),   // afternoon: warm white
                    new GradientColorKey(new Color(1f, 0.98f, 0.95f), 1f)      // noon: near white
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            intensityByElevation = new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.2f, 0.85f), new Keyframe(0.5f, 1f), new Keyframe(1f, 1f));
            // Rays stay visible at ANY sun height (was crushed to 0.06 at the zenith). The low-sun boost remains,
            // but a high/overhead sun still casts shafts — the volumetric module needs this to work at noon.
            rayVisibilityByElevation = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.3f, 0.92f), new Keyframe(0.6f, 0.82f), new Keyframe(1f, 0.75f));
            haloBoostByElevation = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.35f, 0.5f), new Keyframe(1f, 0.15f));
            _defaultsSet = true;
        }
    }
}
