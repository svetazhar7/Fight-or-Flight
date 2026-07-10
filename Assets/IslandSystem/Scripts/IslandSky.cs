using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Our own sky + day/night, replacing COZY entirely. Drives everything from a single <see cref="timeOfDay"/>
    /// (0-24h): the sun directional light (rotation + colour + intensity), the procedural IslandSystem/Skybox
    /// (zenith/horizon/ground/sun/stars), the ambient light and fog colour, and the cloud shader globals (so the
    /// IslandClouds field + StormWall light with the time of day). Everything is gradient-driven with sensible
    /// defaults, so it looks right out of the box; tune the gradients per taste. [ExecuteAlways] so it previews.
    /// </summary>
    [ExecuteAlways]
    public class IslandSky : MonoBehaviour
    {
        [Header("Time")]
        [Range(0f, 24f)] public float timeOfDay = 13f;
        public bool autoAdvance = false;
        [Tooltip("Real minutes for a full 24h day when autoAdvance is on.")]
        public float dayLengthMinutes = 12f;

        [Header("Sun")]
        public Light sun;
        [Tooltip("Compass direction the sun rises/sets along (deg).")]
        public float sunAzimuth = 155f;
        public float maxSunIntensity = 2.0f;
        [Tooltip("Sun light colour over the day (0=midnight .. 1=next midnight).")]
        public Gradient sunColor = new Gradient();
        [Tooltip("Sun intensity fraction over the day (x: 0..1 = time, y: 0..1 = of maxSunIntensity).")]
        public AnimationCurve sunIntensity = AnimationCurve.Linear(0, 0, 1, 0);

        [Header("Sky colours over the day")]
        public Gradient skyZenith = new Gradient();
        public Gradient skyHorizon = new Gradient();
        public Gradient skyGround = new Gradient();
        public Gradient sunDiscColor = new Gradient();
        public Gradient sunGlowColor = new Gradient();

        [Header("Ambient + fog")]
        public Gradient ambientColor = new Gradient();
        [Range(0f, 3f)] public float ambientIntensity = 1f;
        public bool driveFog = true;
        public Gradient fogColor = new Gradient();
        [Tooltip("Exp2 fog density. Gentle haze so distant islands dissolve into the skyline.")]
        public float fogDensity = 0.0012f;

        [Header("Rendering")]
        public Material skyboxMaterial;

        [SerializeField, HideInInspector] bool _defaultsSet;

        static readonly int ID_SkySunDir = Shader.PropertyToID("_SkySunDir");
        static readonly int ID_CloudSunColor = Shader.PropertyToID("_CloudSunColor");
        static readonly int ID_CloudSunDir = Shader.PropertyToID("_CloudSunDir");
        static readonly int ID_CloudAmbient = Shader.PropertyToID("_CloudAmbient");

        void OnEnable() { EnsureDefaults(false); Apply(); }
        void OnValidate() { if (isActiveAndEnabled) Apply(); }

        void Update()
        {
            if (autoAdvance && Application.isPlaying && dayLengthMinutes > 0.01f)
            {
                timeOfDay += (Time.deltaTime / (dayLengthMinutes * 60f)) * 24f;
                if (timeOfDay >= 24f) timeOfDay -= 24f;
            }
            Apply();
        }

        float T => Mathf.Repeat(timeOfDay, 24f) / 24f;   // 0..1 gradient lookup

        public void Apply()
        {
            float t = T;

            // ---- Sun light ----
            if (sun != null)
            {
                // pitch: 6h → 0 (east horizon), 12h → 90 (overhead), 18h → 180 (west horizon), 0h → -90 (below)
                float pitch = (timeOfDay / 24f) * 360f - 90f;
                sun.transform.rotation = Quaternion.Euler(pitch, sunAzimuth, 0f);
                sun.color = sunColor.Evaluate(t);
                sun.intensity = Mathf.Max(0f, sunIntensity.Evaluate(t) * maxSunIntensity);
                sun.enabled = sun.intensity > 0.001f;
            }
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            float sunUp = Mathf.Clamp01(toSun.y);

            // ---- Skybox ----
            if (skyboxMaterial != null)
            {
                skyboxMaterial.SetColor("_ZenithColor", skyZenith.Evaluate(t));
                skyboxMaterial.SetColor("_HorizonColor", skyHorizon.Evaluate(t));
                skyboxMaterial.SetColor("_GroundColor", skyGround.Evaluate(t));
                skyboxMaterial.SetColor("_SunColor", sunDiscColor.Evaluate(t));
                skyboxMaterial.SetColor("_SunGlowColor", sunGlowColor.Evaluate(t));
                skyboxMaterial.SetFloat("_StarStrength", Mathf.Clamp01(1f - sunUp * 3f));   // stars only deep night
                if (RenderSettings.skybox != skyboxMaterial) RenderSettings.skybox = skyboxMaterial;
            }
            Shader.SetGlobalVector(ID_SkySunDir, new Vector4(toSun.x, toSun.y, toSun.z, 0f));

            // ---- Ambient ----
            Color amb = ambientColor.Evaluate(t) * ambientIntensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = amb;
            if (sun != null) RenderSettings.sun = sun;

            // ---- Fog colour (density stays whatever it is; see IslandFog) ----
            if (driveFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor.Evaluate(t);
                RenderSettings.fogDensity = fogDensity;
            }

            // ---- Cloud globals (IslandClouds field + StormWall) ----
            // Clamp to ~1: an intensity-2 sun made the clouds HDR×2 — thin puffs then BRIGHTEN what's behind
            // them (grey-washed island, bloom speckles on water glints when looking down through a cloud).
            Color sc = sun != null ? sun.color * Mathf.Clamp(sun.intensity, 0.12f, 1.05f) : Color.white;
            Shader.SetGlobalColor(ID_CloudSunColor, sc);
            Shader.SetGlobalVector(ID_CloudSunDir, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
            Shader.SetGlobalColor(ID_CloudAmbient, amb);
        }

        // ---- Default gradients (so it looks good with no manual setup) ----
        [ContextMenu("Reset Gradients To Defaults")]
        public void ResetGradients() { EnsureDefaults(true); Apply(); }

        public void EnsureDefaults(bool force)
        {
            if (_defaultsSet && !force) return;   // a fresh AnimationCurve/Gradient already has 2 keys, so use a flag
            sunColor = Grad(("#1a2540", 0f), ("#ff8a3a", 0.27f), ("#fff3df", 0.5f), ("#ff7a30", 0.74f), ("#1a2540", 1f));
            sunIntensity = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.24f, 0f), new Keyframe(0.30f, 0.5f),
                new Keyframe(0.5f, 1f), new Keyframe(0.70f, 0.5f), new Keyframe(0.76f, 0f), new Keyframe(1f, 0f));
            skyZenith = Grad(("#070b1a", 0f), ("#2a3f70", 0.25f), ("#2f6bd0", 0.5f), ("#2a3f70", 0.75f), ("#070b1a", 1f));
            skyHorizon = Grad(("#141d33", 0f), ("#ff9a54", 0.27f), ("#bcd6ef", 0.5f), ("#ff8341", 0.74f), ("#141d33", 1f));
            skyGround = Grad(("#0a0d14", 0f), ("#2a2620", 0.5f), ("#0a0d14", 1f));
            sunDiscColor = Grad(("#000000", 0.22f), ("#ffd9a0", 0.3f), ("#fffaf0", 0.5f), ("#ffcf90", 0.7f), ("#000000", 0.78f));
            sunGlowColor = Grad(("#20130a", 0f), ("#ff8a3a", 0.28f), ("#ffd0a0", 0.5f), ("#ff7a2e", 0.73f), ("#20130a", 1f));
            ambientColor = Grad(("#0c1424", 0f), ("#4a4438", 0.28f), ("#8fa2b8", 0.5f), ("#4a4030", 0.73f), ("#0c1424", 1f));
            fogColor = Grad(("#141d33", 0f), ("#e0a878", 0.27f), ("#bcd0e4", 0.5f), ("#dc9a68", 0.74f), ("#141d33", 1f));
            _defaultsSet = true;
        }

        static Gradient Grad(params (string hex, float t)[] stops)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                ColorUtility.TryParseHtmlString(stops[i].hex, out var c);
                ck[i] = new GradientColorKey(c, stops[i].t);
            }
            g.SetKeys(ck, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
