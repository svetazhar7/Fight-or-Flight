using UnityEngine;

namespace IslandSystem.Sun
{
    /// <summary>
    /// The visible sun disc in the skybox: size, edge softness, limb darkening and HDR brightness (which is what
    /// feeds Bloom). Near the horizon the disc widens and softens automatically (atmospheric scattering flattening),
    /// scaled by the SunSystem's haloBoost curve and atmospheric density.
    /// </summary>
    [DisallowMultipleComponent]
    public class SunDiscModule : SunModuleBase
    {
        public override int Order => 10;

        [Range(0f, 1f), Tooltip("Apparent disc size. 0 = pinpoint, 1 = big stylized sun.")]
        public float discSize = 0.42f;
        [Range(0.0002f, 0.05f), Tooltip("Edge softness of the disc — small = crisp rim, large = airy glow ball.")]
        public float edgeSoftness = 0.006f;
        [Range(0f, 1f), Tooltip("Darkens/curves the disc towards its rim so it reads as a volume, not a flat circle.")]
        public float limbDarkening = 0.35f;
        [Range(1f, 40f), Tooltip("HDR brightness of the disc core — drives how much Bloom picks it up.")]
        public float hdrBrightness = 11f;
        [Range(0f, 1f), Tooltip("Extra brightness kept at the rim so the disc melts into its halo without a hard step.")]
        public float rimGlow = 0.5f;

        [Header("Colour")]
        [Tooltip("Tint the disc with the graded sun light colour (recommended — keeps disc and lighting in sync).")]
        public bool tintFromSunColor = true;
        public Color overrideColor = new Color(1f, 0.94f, 0.82f);
        [Range(0f, 1.5f)] public float saturation = 1f;

        [Header("Atmosphere")]
        [Range(0f, 2f), Tooltip("How much the disc widens + softens near the horizon (scattering flatten).")]
        public float horizonScatter = 0.8f;
        [Range(0f, 1f), Tooltip("How much the disc tightens at high noon (big romantic sun low, small clean sun high).")]
        public float noonShrink = 0.55f;
        [Range(0f, 1f), Tooltip("How much the disc's HDR brightness (and so its bloom ball) eases off at high noon.")]
        public float noonBrightnessFade = 0.45f;

        static readonly int ID_SunColor = Shader.PropertyToID("_SunColor");
        static readonly int ID_SunSize = Shader.PropertyToID("_SunSize");
        static readonly int ID_SunDiscSoft = Shader.PropertyToID("_SunDiscSoft");
        static readonly int ID_SunLimb = Shader.PropertyToID("_SunLimb");
        static readonly int ID_SunRimGlow = Shader.PropertyToID("_SunRimGlow");
        static readonly int ID_SunBrightness = Shader.PropertyToID("_SunBrightness");

        public override void Apply(in SunContext ctx)
        {
            var mat = ctx.skyboxMaterial;
            if (mat == null) return;

            float scatter = ctx.horizonFactor * horizonScatter * ctx.atmosphericDensity;
            float shrunk = discSize * Mathf.Lerp(1f - noonShrink, 1f, ctx.horizonFactor);
            float size = Mathf.Lerp(0.9995f, 0.986f, Mathf.Clamp01(shrunk + scatter * 0.12f));
            float soft = edgeSoftness * (1f + scatter * 2.5f);

            Color c = tintFromSunColor ? ctx.sunColor : overrideColor;
            c = SunColorUtil.AdjustSaturation(c, saturation);

            mat.SetColor(ID_SunColor, c);
            mat.SetFloat(ID_SunSize, size);
            mat.SetFloat(ID_SunDiscSoft, soft);
            mat.SetFloat(ID_SunLimb, limbDarkening);
            mat.SetFloat(ID_SunRimGlow, rimGlow);
            mat.SetFloat(ID_SunBrightness, hdrBrightness * Mathf.Lerp(1f - noonBrightnessFade, 1f, ctx.horizonFactor));
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
        }
    }
}
