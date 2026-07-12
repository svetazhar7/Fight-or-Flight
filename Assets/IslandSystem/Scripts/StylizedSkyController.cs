using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandSystem
{
    /// <summary>
    /// Cozy pastel sky + colour grading in one inspector. Owns the whole "soft warm afternoon" look: the
    /// IslandSystem/Skybox gradient (top/horizon/ground + sun disc), the single sun directional light, flat warm
    /// ambient, gentle Exp2 haze, cloud tint and the URP post volume (ACES + lowered exposure/contrast/saturation,
    /// warm white balance, tiny bloom; vignette/CA/grain forced off). A static "set and forget" replacement for the
    /// time-of-day IslandSky — enabling this disables IslandSky/IslandFog so they stop fighting over RenderSettings.
    /// Everything applies from OnEnable/OnValidate (no per-frame work). SunsetMode presets (Day / Dawn / Golden /
    /// Pink) load the value table from the art brief; after that every field stays hand-tunable — switch the mode to
    /// Custom if you never want a preset to overwrite your tweaks.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class StylizedSkyController : MonoBehaviour
    {
        public enum SunsetMode { Day, Dawn, Golden, Pink, Custom }

        [Header("Preset")]
        [Tooltip("Selecting a preset overwrites the fields below with its table values (Custom never overwrites).")]
        public SunsetMode mode = SunsetMode.Day;

        [Header("Scene references")]
        public Light sun;
        public Material skyboxMaterial;
        public Volume postFxVolume;
        public IslandClouds clouds;

        [Header("Sky")]
        public Color topSkyColor = new Color(0.55f, 0.72f, 0.96f);
        public Color horizonColor = new Color(1f, 0.90f, 0.72f);
        public Color groundColor = new Color(0.95f, 0.90f, 0.85f);
        [Range(0f, 1f), Tooltip("0 = crisp horizon band, 1 = very soft wash from horizon up to the zenith.")]
        public float skyGradientStrength = 0.45f;
        [Range(0f, 1.5f), Tooltip("Quick pastel dial: scales the saturation of the three sky colours (1 = as authored).")]
        public float skySaturation = 1f;

        [Header("Sun")]
        public Color sunColor = new Color(1f, 0.95f, 0.88f);
        [Range(0f, 3f)] public float sunIntensity = 1.15f;
        [Range(2f, 90f), Tooltip("Elevation above the horizon (deg). Low = long soft sunset shadows.")]
        public float sunAngle = 45f;
        [Range(0f, 360f), Tooltip("Compass direction of the sun. Kept out of the presets — it is scene composition.")]
        public float sunAzimuth = 155f;
        [Range(0f, 1f), Tooltip("Visual disc size in the skybox (angular-diameter stand-in). Bigger = softer glowing sun.")]
        public float sunDiscSize = 0.35f;
        [Range(1f, 40f), Tooltip("HDR brightness of the disc — this is what feeds Bloom.")]
        public float sunHdrBrightness = 10f;
        [Range(0f, 3f)] public float sunGlowAmount = 0.4f;
        public Color sunGlowColor = new Color(1f, 0.85f, 0.60f);
        [Range(0f, 1f), Tooltip("Below 1 keeps shadows light and airy instead of pitch black.")]
        public float shadowStrength = 0.8f;

        [Header("Ambient")]
        public Color ambientColor = new Color(1f, 0.95f, 0.90f);
        [Range(0f, 2f)] public float ambientIntensity = 0.55f;

        [Header("Fog")]
        public bool enableFog = true;
        [Tooltip("Derive the fog colour from the horizon colour so distant terrain melts into the sky.")]
        public bool matchFogToHorizon = true;
        public Color fogColor = new Color(1f, 0.90f, 0.75f);
        [Min(0f), Tooltip("Exp2 density. 0.0001–0.002 = gentle distance haze.")]
        public float fogDensity = 0.0002f;
        [Range(0f, 1f), Tooltip("Extra horizon haze: widens the horizon colour band and thickens the fog a touch.")]
        public float horizonHaze = 0.25f;

        [Header("Clouds")]
        [Range(0f, 2f), Tooltip("Scales the cloud ambient tint below (sun-side brightness follows the sun light itself).")]
        public float cloudBrightness = 1f;
        public Color cloudAmbientColor = new Color(0.45f, 0.44f, 0.50f);
        [Range(0f, 1f), Tooltip("0 = crisp cutout puffs, 1 = airy feathered edges.")]
        public float cloudSoftness = 0.2f;

        [Header("Colour grading (URP volume)")]
        [Tooltip("Filmic ACES tonemapping; the lowered contrast below pulls its default punch back down.")]
        public bool acesTonemapping = true;
        [Range(-3f, 1f)] public float postExposure = -0.5f;
        [Range(-100f, 100f), Tooltip("URP units. The brief's 0.7–0.9 'contrast' ≈ −30…−10 here.")]
        public float contrast = -15f;
        [Range(-100f, 100f), Tooltip("URP units. The brief's 0.8–0.9 'saturation' ≈ −20…−10 here.")]
        public float saturation = -12f;
        [Range(-100f, 100f), Tooltip("White-balance warmth. Positive = warmer (the brief's +200…+800K ≈ +5…+20).")]
        public float temperature = 8f;
        [Range(-100f, 100f)] public float tint = 0f;
        [Tooltip("Very light warm veil over the whole frame. Keep it barely visible.")]
        public Color colorFilter = new Color(1f, 0.97f, 0.92f);

        [Header("Bloom")]
        [Range(0f, 2f)] public float bloomIntensity = 0.15f;
        [Range(0f, 1f)] public float bloomScatter = 0.6f;
        [Range(0f, 3f)] public float bloomThreshold = 1.1f;

        [SerializeField, HideInInspector] SunsetMode _loadedPreset = (SunsetMode)(-1);

        static readonly int ID_ZenithColor   = Shader.PropertyToID("_ZenithColor");
        static readonly int ID_HorizonColor  = Shader.PropertyToID("_HorizonColor");
        static readonly int ID_GroundColor   = Shader.PropertyToID("_GroundColor");
        static readonly int ID_HorizonSharp  = Shader.PropertyToID("_HorizonSharp");
        static readonly int ID_SunColor      = Shader.PropertyToID("_SunColor");
        static readonly int ID_SunSize       = Shader.PropertyToID("_SunSize");
        static readonly int ID_SunBrightness = Shader.PropertyToID("_SunBrightness");
        static readonly int ID_SunGlow       = Shader.PropertyToID("_SunGlow");
        static readonly int ID_SunGlowColor  = Shader.PropertyToID("_SunGlowColor");
        static readonly int ID_StarStrength  = Shader.PropertyToID("_StarStrength");
        static readonly int ID_SkySunDir     = Shader.PropertyToID("_SkySunDir");

        void OnEnable()
        {
            TakeOverLegacyControllers();
            if (mode != SunsetMode.Custom && _loadedPreset != mode) LoadPreset(mode);
            ApplyAll();
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            if (mode != SunsetMode.Custom && _loadedPreset != mode) LoadPreset(mode);
            ApplyAll();
        }

        Sun.SunSystem _sunSystem;   // when a SunSystem exists it owns the sun light, disc, halo and atmosphere

        [ContextMenu("Reapply")]
        public void ApplyAll()
        {
            _sunSystem = FindFirstObjectByType<Sun.SunSystem>();
            ApplySun();
            ApplySkybox();
            ApplyAmbientAndFog();
            ApplyClouds();
            ApplyPostFx();
            // The sun system layers its modulation (ambient tint, fog warmth, halo, rays) on top of our base values.
            if (_sunSystem != null && _sunSystem.isActiveAndEnabled) _sunSystem.ApplyAll();
        }

        [ContextMenu("Reload Preset Values")]
        void ReloadPresetValues()
        {
            if (mode == SunsetMode.Custom) return;
            LoadPreset(mode);
            ApplyAll();
        }

        /// <summary>The old per-frame controllers would stomp everything we set — switch them off once.</summary>
        void TakeOverLegacyControllers()
        {
            var legacySky = FindFirstObjectByType<IslandSky>(FindObjectsInactive.Include);
            if (legacySky != null && legacySky.enabled)
            {
                legacySky.enabled = false;
                Debug.Log("[StylizedSky] Disabled IslandSky — StylizedSkyController owns the sky now.", legacySky);
            }
            var legacyFog = FindFirstObjectByType<IslandFog>(FindObjectsInactive.Include);
            if (legacyFog != null && legacyFog.enabled)
            {
                legacyFog.enabled = false;
                Debug.Log("[StylizedSky] Disabled IslandFog — fog is driven here.", legacyFog);
            }
        }

        void ApplySun()
        {
            if (_sunSystem != null && _sunSystem.isActiveAndEnabled) return;   // SunSystem owns the light
            if (sun == null) return;
            sun.transform.rotation = Quaternion.Euler(sunAngle, sunAzimuth, 0f);
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.enabled = sunIntensity > 0.001f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = shadowStrength;
            RenderSettings.sun = sun;
            Vector3 toSun = -sun.transform.forward;
            Shader.SetGlobalVector(ID_SkySunDir, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
        }

        void ApplySkybox()
        {
            if (skyboxMaterial == null) return;
            skyboxMaterial.SetColor(ID_ZenithColor, Pastel(topSkyColor));
            skyboxMaterial.SetColor(ID_HorizonColor, Pastel(horizonColor));
            skyboxMaterial.SetColor(ID_GroundColor, Pastel(groundColor));
            // Gradient softness: a low _HorizonSharp lets the horizon colour wash far up the dome; haze softer still.
            float sharp = Mathf.Lerp(4.5f, 0.7f, skyGradientStrength) / (1f + horizonHaze);
            skyboxMaterial.SetFloat(ID_HorizonSharp, Mathf.Clamp(sharp, 0.5f, 8f));
            if (_sunSystem == null || !_sunSystem.isActiveAndEnabled)
            {
                // Legacy path: no SunSystem — this controller paints the sun disc/glow itself.
                skyboxMaterial.SetColor(ID_SunColor, sunColor);
                skyboxMaterial.SetFloat(ID_SunSize, Mathf.Lerp(0.9995f, 0.988f, sunDiscSize));
                skyboxMaterial.SetFloat(ID_SunBrightness, sunHdrBrightness);
                skyboxMaterial.SetFloat(ID_SunGlow, sunGlowAmount);
                skyboxMaterial.SetColor(ID_SunGlowColor, sunGlowColor);
            }
            skyboxMaterial.SetFloat(ID_StarStrength, 0f);
            if (RenderSettings.skybox != skyboxMaterial) RenderSettings.skybox = skyboxMaterial;
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(skyboxMaterial);
#endif
        }

        Color Pastel(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * skySaturation), v);
        }

        void ApplyAmbientAndFog()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor * ambientIntensity;

            if (matchFogToHorizon) fogColor = Color.Lerp(horizonColor, Color.white, 0.15f);
            RenderSettings.fog = enableFog;
            if (!enableFog) return;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity * (1f + horizonHaze);
        }

        void ApplyClouds()
        {
            // Sun-side cloud colour/direction follow RenderSettings.sun inside IslandClouds itself; we only tint
            // the shadow-side ambient and the silhouette softness.
            if (clouds == null) return;
            clouds.ambient = cloudAmbientColor * cloudBrightness;
            clouds.edgeSoftness = Mathf.Lerp(0.03f, 0.4f, cloudSoftness);
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(clouds);
#endif
        }

        void ApplyPostFx()
        {
            // SunnyColorGrading owns the whole post look when present (selective warm grade + custom bloom) —
            // writing our global warm filter on top of it would be exactly the "yellow veil" it exists to kill.
            var grading = FindFirstObjectByType<Sun.SunnyColorGrading>();
            if (grading != null && grading.isActiveAndEnabled) { grading.ApplyAll(); return; }

            var profile = postFxVolume != null ? postFxVolume.sharedProfile : null;
            if (profile == null) return;

            var tone = GetOrAdd<Tonemapping>(profile);
            tone.active = true;
            tone.mode.overrideState = true;
            tone.mode.value = acesTonemapping ? TonemappingMode.ACES : TonemappingMode.Neutral;

            var ca = GetOrAdd<ColorAdjustments>(profile);
            ca.active = true;
            Set(ca.postExposure, postExposure);
            Set(ca.contrast, contrast);
            Set(ca.saturation, saturation);
            ca.colorFilter.overrideState = true;
            ca.colorFilter.value = colorFilter;

            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.active = true;
            Set(wb.temperature, temperature);
            Set(wb.tint, tint);

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.active = true;
            Set(bloom.intensity, bloomIntensity);
            Set(bloom.scatter, bloomScatter);
            Set(bloom.threshold, bloomThreshold);

            // The cozy brief keeps these off. Only touched when they already exist in the profile.
            if (profile.TryGet(out Vignette vig)) vig.active = false;
            if (profile.TryGet(out ChromaticAberration cab)) cab.active = false;
            if (profile.TryGet(out FilmGrain grain)) grain.active = false;
            if (profile.TryGet(out LensDistortion dist)) dist.active = false;

#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(profile);
#endif
        }

        static void Set(FloatParameter p, float v)
        {
            p.overrideState = true;
            p.value = v;
        }

        T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T comp)) return comp;
            comp = profile.Add<T>(true);
#if UNITY_EDITOR
            // A component added to a profile ASSET must be attached as a sub-asset or it is lost on reload.
            if (!Application.isPlaying && AssetDatabase.Contains(profile))
            {
                AssetDatabase.AddObjectToAsset(comp, profile);
                EditorUtility.SetDirty(profile);
            }
#endif
            return comp;
        }

        void LoadPreset(SunsetMode m)
        {
            switch (m)
            {
                case SunsetMode.Day:
                    topSkyColor = new Color(0.55f, 0.72f, 0.96f); horizonColor = new Color(1f, 0.90f, 0.72f); groundColor = new Color(0.95f, 0.90f, 0.85f);
                    skyGradientStrength = 0.45f; skySaturation = 1f;
                    sunColor = new Color(1f, 0.95f, 0.88f); sunIntensity = 1.15f; sunAngle = 45f; sunDiscSize = 0.35f;
                    sunHdrBrightness = 10f; sunGlowAmount = 0.35f; sunGlowColor = new Color(1f, 0.85f, 0.60f); shadowStrength = 0.8f;
                    ambientColor = new Color(1f, 0.95f, 0.90f); ambientIntensity = 0.55f;
                    fogDensity = 0.0002f; horizonHaze = 0.25f;
                    cloudBrightness = 1f; cloudAmbientColor = new Color(0.45f, 0.44f, 0.50f); cloudSoftness = 0.2f;
                    postExposure = -0.5f; contrast = -15f; saturation = -12f; temperature = 8f; tint = 0f;
                    colorFilter = new Color(1f, 0.97f, 0.92f);
                    bloomIntensity = 0.15f; bloomScatter = 0.6f; bloomThreshold = 1.1f;
                    break;

                case SunsetMode.Dawn:
                    topSkyColor = new Color(0.55f, 0.68f, 0.92f); horizonColor = new Color(1f, 0.70f, 0.50f); groundColor = new Color(0.90f, 0.78f, 0.68f);
                    skyGradientStrength = 0.55f; skySaturation = 0.95f;
                    sunColor = new Color(1f, 0.80f, 0.60f); sunIntensity = 0.9f; sunAngle = 15f; sunDiscSize = 0.45f;
                    sunHdrBrightness = 12f; sunGlowAmount = 0.6f; sunGlowColor = new Color(1f, 0.70f, 0.45f); shadowStrength = 0.7f;
                    ambientColor = new Color(1f, 0.90f, 0.80f); ambientIntensity = 0.55f;
                    fogDensity = 0.00035f; horizonHaze = 0.35f;
                    cloudBrightness = 1f; cloudAmbientColor = new Color(0.55f, 0.47f, 0.50f); cloudSoftness = 0.25f;
                    postExposure = -0.75f; contrast = -22f; saturation = -18f; temperature = 14f; tint = 0f;
                    colorFilter = new Color(1f, 0.92f, 0.76f);
                    bloomIntensity = 0.25f; bloomScatter = 0.7f; bloomThreshold = 0.95f;
                    break;

                case SunsetMode.Golden:
                    topSkyColor = new Color(0.92f, 0.72f, 0.45f); horizonColor = new Color(0.90f, 0.52f, 0.62f); groundColor = new Color(0.85f, 0.68f, 0.50f);
                    skyGradientStrength = 0.7f; skySaturation = 1f;
                    sunColor = new Color(1f, 0.70f, 0.32f); sunIntensity = 1.3f; sunAngle = 10f; sunDiscSize = 0.55f;
                    sunHdrBrightness = 14f; sunGlowAmount = 0.8f; sunGlowColor = new Color(1f, 0.62f, 0.30f); shadowStrength = 0.75f;
                    ambientColor = new Color(1f, 0.90f, 0.78f); ambientIntensity = 0.62f;
                    fogDensity = 0.0004f; horizonHaze = 0.45f;
                    cloudBrightness = 1.1f; cloudAmbientColor = new Color(0.55f, 0.45f, 0.40f); cloudSoftness = 0.22f;
                    postExposure = -0.4f; contrast = -15f; saturation = -10f; temperature = 12f; tint = 0f;
                    colorFilter = new Color(1f, 0.90f, 0.78f);
                    bloomIntensity = 0.3f; bloomScatter = 0.7f; bloomThreshold = 0.9f;
                    break;

                case SunsetMode.Pink:
                    topSkyColor = new Color(0.95f, 0.55f, 0.72f); horizonColor = new Color(0.62f, 0.78f, 1f); groundColor = new Color(0.85f, 0.75f, 0.82f);
                    skyGradientStrength = 0.72f; skySaturation = 0.95f;
                    sunColor = new Color(1f, 0.55f, 0.78f); sunIntensity = 1.1f; sunAngle = 6f; sunDiscSize = 0.5f;
                    sunHdrBrightness = 12f; sunGlowAmount = 0.7f; sunGlowColor = new Color(1f, 0.60f, 0.80f); shadowStrength = 0.7f;
                    ambientColor = new Color(1f, 0.90f, 0.85f); ambientIntensity = 0.55f;
                    fogDensity = 0.0004f; horizonHaze = 0.4f;
                    cloudBrightness = 1f; cloudAmbientColor = new Color(0.55f, 0.45f, 0.55f); cloudSoftness = 0.25f;
                    postExposure = -0.6f; contrast = -15f; saturation = -10f; temperature = 8f; tint = 0f;
                    colorFilter = new Color(1f, 0.90f, 0.85f);
                    bloomIntensity = 0.25f; bloomScatter = 0.7f; bloomThreshold = 0.95f;
                    break;
            }
            _loadedPreset = m;
        }
    }
}
