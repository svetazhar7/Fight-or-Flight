using UnityEngine;

namespace IslandSystem.Sun
{
    /// <summary>
    /// TRUE volumetric sun shafts — the "light streaming onto the glade / between the leaves" effect. A ray-march
    /// pass (SunVolumetricLightFeature) samples the MAIN LIGHT SHADOW MAP along each view ray and accumulates the
    /// sunlight scattered by the air in the LIT segments. Unlike the screen-space god rays (a glow that needs the
    /// sun on screen and fades at high sun), this is visible from any view and at any sun elevation — including the
    /// zenith — and it is occluded for free: a tree or a cloud (the cloud shadow is baked into the sun's shadows)
    /// that shadows the air darkens that column of the shaft.
    ///
    /// This module only holds the settings + the per-apply fade; the render feature reads it through <see cref="Active"/>.
    /// Clouds covering the sun fade the whole effect out via the shared cloudRayFade (same signal as the god rays).
    /// </summary>
    [DisallowMultipleComponent]
    public class SunVolumetricModule : SunRayModeBase
    {
        public override int Order => 48;

        public static SunVolumetricModule Active { get; private set; }

        public enum Downsample { Full = 0, Half = 1, Quarter = 2 }

        [Header("Master")]
        [Range(0f, 4f), Tooltip("Overall brightness of the volumetric shafts in the composite.")]
        public float intensity = 1.15f;
        [Range(0f, 4f), Tooltip("Scattering density — how much light the air catches per metre.")]
        public float density = 1.05f;
        [Tooltip("Tint the shafts from the graded sun colour (recommended).")]
        public bool tintFromSunColor = true;
        public Color overrideColor = new Color(1f, 0.9f, 0.72f);
        [Range(0f, 1.5f)] public float saturation = 1f;

        [Header("Shape")]
        [Range(0f, 0.95f), Tooltip("Directionality: 0 = shafts equally visible everywhere, high = bright beams only when looking toward the sun.")]
        public float anisotropy = 0.62f;
        [Range(0f, 1f), Tooltip("Isotropic floor: keeps shafts visible on the glade even when NOT looking at the sun. Too high = milky fog.")]
        public float ambientScatter = 0.22f;

        [Header("Range & falloff")]
        [Range(20f, 600f), Tooltip("How far the march goes (metres). Shorter = denser near shafts, cheaper.")]
        public float maxDistance = 200f;
        [Range(0f, 0.1f), Tooltip("Height falloff: shafts thin out with altitude so the fog pools low (0 = uniform).")]
        public float heightFalloff = 0.012f;
        [Tooltip("World height where the volumetric fog is thickest (the ground level of the play area).")]
        public float groundHeight = 60f;

        [Header("Quality")]
        [Range(8, 96), Tooltip("March steps. More = smoother shafts, higher cost.")]
        public int steps = 40;
        [Range(0, 3), Tooltip("Blur passes over the half-res shafts (hides the march dither).")]
        public int blurIterations = 1;
        [Tooltip("Render scale. Half is the quality/cost sweet spot.")]
        public Downsample downsample = Downsample.Half;

        // ---- Values consumed by SunVolumetricLightFeature (computed in Apply) ----
        public Vector3 ToSunWorld { get; private set; } = Vector3.up;
        public Color ShaftColor { get; private set; } = Color.white;
        public float FinalIntensity { get; private set; }

        protected override void OnEnable() { Active = this; base.OnEnable(); }
        protected override void OnDisable() { if (Active == this) Active = null; base.OnDisable(); }

        public override void Apply(in SunContext ctx)
        {
            ToSunWorld = ctx.toSun;
            Color c = tintFromSunColor ? ctx.sunColor : overrideColor;
            ShaftColor = SunColorUtil.AdjustSaturation(c, saturation);
            // Clouds over the sun kill the shafts (same signal the god rays use); no sun = no shafts.
            FinalIntensity = intensity * ctx.cloudRayFade * (ctx.sunIntensity > 0.001f ? 1f : 0f);
        }
    }
}
