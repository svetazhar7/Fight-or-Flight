using UnityEngine;

namespace IslandSystem.Sun
{
    /// <summary>
    /// Screen Space Light Shafts (god rays) — the primary ray mode. A radial-blur post pass (SunGodRaysFeature)
    /// smears the sky mask away from the sun's screen position; anything that writes depth (terrain, trees, props)
    /// carves the mask, which is exactly what produces the light bands between branches and hills with no extra
    /// setup. Animated procedural noise keeps the air subtly alive, and angular "streaks" fake a beam count/width
    /// dial that pure screen-space blur doesn't naturally have. This component only holds the settings and computes
    /// the per-apply fade (elevation curve × haze); the render feature reads it through <see cref="Active"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class SunGodRaysModule : SunRayModeBase
    {
        public override int Order => 50;

        public static SunGodRaysModule Active { get; private set; }

        public enum Downsample { Full = 0, Half = 1, Quarter = 2 }

        [Header("Master")]
        [Range(0f, 4f), Tooltip("Overall ray intensity in the composite.")]
        public float intensity = 0.9f;
        [Range(0f, 4f), Tooltip("Brightness (exposure) of the accumulated shafts before compositing.")]
        public float brightness = 1.1f;
        [Range(0f, 1f), Tooltip("Opacity of the composite (1 = fully additive).")]
        public float opacity = 1f;

        [Header("Shape")]
        [Range(0.1f, 1f), Tooltip("Ray length — how far the shafts smear across the screen.")]
        public float rayLength = 0.85f;
        [Range(0.05f, 1.5f), Tooltip("Max screen distance from the sun that still contributes light (mask radius).")]
        public float maxDistance = 0.85f;
        [Range(0.8f, 0.99f), Tooltip("Decay exponent per sample — high = long airy rays, low = short punchy ones.")]
        public float falloffExponent = 0.95f;
        [Range(0.25f, 4f), Tooltip("Sharpness of the source mask around the sun (contrast of the bright region).")]
        public float sharpness = 1.6f;
        [Range(0f, 2f), Tooltip("Scattering boost applied while accumulating samples (weight).")]
        public float scattering = 1.15f;
        [Range(0f, 1f), Tooltip("Edge softness of the shafts (0 = crisp bands, 1 = misty wash).")]
        public float edgeSoftness = 0.35f;

        [Header("Streaks (artistic beam structure)")]
        [Range(0f, 64f), Tooltip("Number of angular beams carved into the light (0 = smooth radial glow).")]
        public float streakCount = 14f;
        [Range(0f, 1f), Tooltip("How visible the beam structure is.")]
        public float streakStrength = 0.3f;
        [Range(0.5f, 8f), Tooltip("Sharpness of each beam — thin crisp spokes vs broad soft wedges.")]
        public float streakSharpness = 2f;

        [Header("Animation (living air)")]
        [Range(0f, 2f), Tooltip("Speed of the procedural noise drift.")]
        public float animationSpeed = 0.35f;
        [Range(0.5f, 40f), Tooltip("Scale of the procedural noise.")]
        public float noiseScale = 6f;
        [Range(0f, 1f), Tooltip("Strength of the noise — sample jitter + slow shimmer of the beams.")]
        public float noiseStrength = 0.35f;

        [Header("Colour")]
        [Tooltip("Tint rays from the graded sun colour (recommended).")]
        public bool tintFromSunColor = true;
        public Color overrideColor = new Color(1f, 0.9f, 0.72f);
        [Range(0f, 1.5f)] public float saturation = 1f;
        [Range(-1f, 1f), Tooltip("Warm/cool shift relative to the sun colour.")]
        public float temperatureShift = 0.1f;

        [Header("Quality")]
        [Range(8, 48), Tooltip("Radial samples per blur iteration.")]
        public int sampleCount = 24;
        [Range(1, 3), Tooltip("Blur iterations — each one multiplies the effective sample count.")]
        public int blurIterations = 2;
        [Tooltip("Render scale of the effect. Half is the quality/cost sweet spot.")]
        public Downsample downsample = Downsample.Half;

        [Header("Atmosphere coupling")]
        [Range(0f, 2f), Tooltip("How much scene haze (fog density + atmospheric density) feeds ray visibility.")]
        public float hazeInfluence = 1f;
        [Range(0f, 1f), Tooltip("Obey the SunSystem's ray-visibility-by-elevation curve (1 = fully).")]
        public float elevationInfluence = 1f;
        [Range(0.5f, 6f), Tooltip("How quickly rays fade as the sun leaves the screen edge.")]
        public float screenEdgeFade = 2f;

        // ---- Values consumed by SunGodRaysFeature (computed in Apply) ----
        public Vector3 ToSunWorld { get; private set; } = Vector3.up;
        public Color RayColor { get; private set; } = Color.white;
        public float FinalIntensity { get; private set; }

        protected override void OnEnable() { Active = this; base.OnEnable(); }
        protected override void OnDisable() { if (Active == this) Active = null; base.OnDisable(); }

        public override void Apply(in SunContext ctx)
        {
            ToSunWorld = ctx.toSun;

            Color c = tintFromSunColor ? ctx.sunColor : overrideColor;
            c = SunColorUtil.ShiftTemperature(c, temperatureShift);
            RayColor = SunColorUtil.AdjustSaturation(c, saturation);

            float elevationFade = Mathf.Lerp(1f, ctx.rayVisibility, elevationInfluence);
            float haze = Mathf.Lerp(1f, Mathf.Clamp(ctx.atmosphericDensity, 0.25f, 2f), Mathf.Clamp01(hazeInfluence));
            FinalIntensity = intensity * elevationFade * haze * (ctx.sunIntensity > 0.001f ? 1f : 0f);
        }
    }
}
