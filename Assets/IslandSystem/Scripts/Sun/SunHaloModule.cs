using UnityEngine;

namespace IslandSystem.Sun
{
    /// <summary>
    /// The layered glow around the sun disc: a tight halo ring (inner/outer radius in degrees) plus the wide
    /// atmospheric glow that washes along the sky. Both are drawn by the skybox shader; this module only feeds the
    /// numbers. Near the horizon the halo grows and brightens automatically (haloBoost curve × scattering influence)
    /// so a low sun sits in a warm luminous pocket of air.
    /// </summary>
    [DisallowMultipleComponent]
    public class SunHaloModule : SunModuleBase
    {
        public override int Order => 20;

        [Header("Halo ring (tight glow hugging the disc)")]
        [Range(0f, 3f)] public float haloIntensity = 0.9f;
        [Range(0.5f, 20f), Tooltip("Inner radius of the halo in degrees from the sun centre.")]
        public float innerRadius = 1.5f;
        [Range(2f, 60f), Tooltip("Outer radius where the halo has fully faded.")]
        public float outerRadius = 14f;
        [Range(0.05f, 1f), Tooltip("Softness of the halo falloff (higher = mistier).")]
        public float softness = 0.65f;
        [Range(0f, 1f)] public float opacity = 0.85f;
        [Range(0.5f, 6f), Tooltip("Falloff exponent — how fast the halo dies towards the outer radius.")]
        public float falloffPower = 2.2f;

        [Header("Wide atmospheric glow (sky-wash towards the sun)")]
        [Range(0f, 3f)] public float glowAmount = 0.55f;
        [Range(4f, 128f), Tooltip("Tightness of the wide glow lobe (higher = narrower).")]
        public float glowExponent = 30f;

        [Header("Colour")]
        [Tooltip("Derive halo colour from the graded sun colour (recommended).")]
        public bool tintFromSunColor = true;
        public Color overrideColor = new Color(1f, 0.83f, 0.6f);
        [Range(0f, 1.5f)] public float saturation = 1.05f;
        [Range(-1f, 1f), Tooltip("Warm/cool shift of the halo relative to the sun colour.")]
        public float temperatureShift = 0.15f;

        [Header("Coupling")]
        [Range(0f, 4f), Tooltip("HDR multiplier so the halo core feeds Bloom a little (keep low — Bloom is air, not glare).")]
        public float bloomFeed = 1.4f;
        [Range(0f, 2f), Tooltip("How much the horizon/atmosphere boost enlarges + brightens the halo.")]
        public float scatteringInfluence = 1f;
        [Range(0f, 1f), Tooltip("How much the halo + wide glow ease off at high noon (prevents a blown white ball overhead).")]
        public float noonFade = 0.6f;

        static readonly int ID_HaloColor = Shader.PropertyToID("_HaloColor");
        static readonly int ID_HaloParams = Shader.PropertyToID("_HaloParams");   // x inner cos, y outer cos, z softness, w falloff
        static readonly int ID_HaloIntensity = Shader.PropertyToID("_HaloIntensity");
        static readonly int ID_SunGlow = Shader.PropertyToID("_SunGlow");
        static readonly int ID_SunGlowColor = Shader.PropertyToID("_SunGlowColor");
        static readonly int ID_SunGlowExp = Shader.PropertyToID("_SunGlowExp");

        public override void Apply(in SunContext ctx)
        {
            var mat = ctx.skyboxMaterial;
            if (mat == null) return;

            float boost = 1f + ctx.haloBoost * scatteringInfluence * ctx.atmosphericDensity;
            boost *= Mathf.Lerp(1f - noonFade, 1f, ctx.horizonFactor);

            Color c = tintFromSunColor ? ctx.sunColor : overrideColor;
            c = SunColorUtil.ShiftTemperature(c, temperatureShift);
            c = SunColorUtil.AdjustSaturation(c, saturation);

            float inner = Mathf.Cos(Mathf.Deg2Rad * innerRadius);
            float outer = Mathf.Cos(Mathf.Deg2Rad * Mathf.Max(outerRadius * boost, innerRadius + 0.5f));

            mat.SetColor(ID_HaloColor, c * bloomFeed);
            mat.SetVector(ID_HaloParams, new Vector4(inner, outer, softness, falloffPower));
            mat.SetFloat(ID_HaloIntensity, haloIntensity * opacity * boost);
            mat.SetFloat(ID_SunGlow, glowAmount * boost);
            mat.SetColor(ID_SunGlowColor, c);
            mat.SetFloat(ID_SunGlowExp, glowExponent);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
#endif
        }
    }
}
