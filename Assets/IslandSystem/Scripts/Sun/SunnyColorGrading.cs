using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IslandSystem.Sun
{
    /// <summary>
    /// The "warm summer day" colour grade — one inspector that owns the whole post look, in two layers:
    ///
    /// URP VOLUME layer (global, hue-agnostic): tonemapping (Neutral = soft highlight roll-off), exposure,
    /// contrast, saturation, hue shift, colour filter, white balance, lift/gamma/gain, shadows/midtones/highlights.
    ///
    /// SELECTIVE layer (custom SunnyGradeFeature pass, the reason this exists): warmth that is applied BY HUE —
    /// golden highlights, cool shadows, warm-only saturation and a warm golden bloom, all gated by blue
    /// preservation / sky protection / cold-colour preservation masks. The result: sunlit grass, foliage and
    /// bright surfaces glow golden while the blue sky and cold materials keep their clean, saturated hue —
    /// warm sunlight, not a yellow filter over the frame.
    ///
    /// The custom bloom REPLACES URP bloom (which can only tint globally): quadratic-knee extract, golden tint
    /// weighted by source warmth, colour isolation so cold brights bloom their own colour or not at all.
    /// Everything applies live from OnValidate; the feature reads <see cref="Active"/> every frame.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SunnyColorGrading : MonoBehaviour
    {
        public static SunnyColorGrading Active { get; private set; }

        public enum ToneMode { None, Neutral, ACES }

        [Header("Master")]
        [Range(0f, 1f), Tooltip("Blend of the whole selective grade + custom bloom (0 = bypass the custom pass).")]
        public float effectStrength = 1f;

        [Header("Exposure & tone (URP volume)")]
        [Range(0.25f, 2f), Tooltip("Linear pre-grade exposure inside the custom pass (1 = neutral).")]
        public float exposure = 1f;
        [Range(-3f, 3f), Tooltip("URP post exposure (EV), applied by the volume after the custom pass.")]
        public float postExposure = -0.2f;
        [Range(-100f, 100f)] public float contrast = 8f;
        [Range(-100f, 100f)] public float saturation = 6f;
        [Range(-180f, 180f)] public float hueShift = 0f;
        [Tooltip("Global multiply filter. KEEP WHITE for the clean-air look — warmth comes from the selective layer.")]
        public Color colorFilter = Color.white;
        [Tooltip("Neutral = modern soft roll-off (recommended: keeps colours clean). ACES is punchier but darkens and warps hues.")]
        public ToneMode tonemapping = ToneMode.Neutral;

        [Header("White balance (URP volume — keep near 0, warmth is selective!)")]
        [Range(-100f, 100f)] public float whiteBalanceTemperature = 0f;
        [Range(-100f, 100f)] public float whiteBalanceTint = 0f;

        [Header("Lift / Gamma / Gain (URP volume)")]
        public Color liftColor = Color.white;
        [Range(-1f, 1f)] public float lift = 0f;
        public Color gammaColor = Color.white;
        [Range(-1f, 1f)] public float gamma = 0f;
        public Color gainColor = Color.white;
        [Range(-1f, 1f)] public float gain = 0f;

        [Header("Shadows / Midtones / Highlights (URP volume)")]
        public Color shadowsColor = Color.white;
        [Range(-1f, 1f)] public float shadowsOffset = 0f;
        public Color midtonesColor = Color.white;
        [Range(-1f, 1f)] public float midtonesOffset = 0f;
        public Color highlightsColor = Color.white;
        [Range(-1f, 1f)] public float highlightsOffset = 0f;

        [Header("Selective warmth (custom pass — the sunny-day core)")]
        [Range(0f, 1f), Tooltip("Golden tint on bright areas (the 'air full of light' feel).")]
        public float highlightWarmth = 0.4f;
        [Range(0f, 2f), Tooltip("Strength multiplier of the warm highlight push.")]
        public float warmHighlightStrength = 1f;
        [Tooltip("The golden sunlight colour highlights lean toward.")]
        public Color warmColor = new Color(1f, 0.87f, 0.62f);
        [Range(0f, 1f), Tooltip("Cool tint on shadowed areas — natural warm-light/cool-shade contrast.")]
        public float shadowCoolness = 0.22f;
        [Tooltip("The cool shade colour. Keep it near-neutral — shadows must not go deep blue.")]
        public Color coolColor = new Color(0.93f, 0.97f, 1.06f);
        [Range(0f, 1f), Tooltip("Extra saturation on WARM hues only (juicy sunlit greens/yellows; cold hues untouched).")]
        public float warmSaturation = 0.18f;

        [Header("Cold colour protection (what keeps the sky clean)")]
        [Range(0f, 1f), Tooltip("Cold hues opt out of ALL warm operations by this amount.")]
        public float coldColorPreservation = 0.85f;
        [Range(0f, 1f), Tooltip("Saturated blues (water, blue props) are protected from warm tinting.")]
        public float bluePreservation = 0.85f;
        [Range(0f, 1f), Tooltip("Bright blues (the sky) get an extra protection layer on top of blue preservation.")]
        public float skyColorProtection = 0.9f;

        [Header("Structure (custom pass)")]
        [Range(0f, 1f), Tooltip("Local contrast (unsharp on luma) — volume and depth without global harshness.")]
        public float localContrast = 0.15f;
        [Range(0f, 1f), Tooltip("Soft roll-off of super-bright areas before tonemapping — no hard clipping.")]
        public float highlightCompression = 0.35f;
        [Range(0f, 0.2f), Tooltip("Tinted fill floor in the darks — blacks never crush, detail survives.")]
        public float shadowLift = 0.03f;
        [Tooltip("Colour of the shadow fill floor.")]
        public Color shadowLiftColor = new Color(0.85f, 0.9f, 1f);

        [Header("Bloom (custom warm bloom — replaces URP bloom)")]
        [Range(0f, 3f), Tooltip("Bloom brightness in the composite.")]
        public float bloomIntensity = 0.65f;
        [Range(0f, 3f), Tooltip("HDR threshold — what counts as 'bright enough to glow'.")]
        public float bloomThreshold = 1f;
        [Range(0f, 1f), Tooltip("How far the glow spreads (extra blur passes + growing radius).")]
        public float bloomScatter = 0.55f;
        [Range(0.25f, 3f), Tooltip("Base blur radius in texels — the size of the soft halo.")]
        public float bloomRadius = 1.2f;
        [Tooltip("Golden tint of the bloom on WARM sources (cold sources keep their own colour).")]
        public Color bloomTint = new Color(1f, 0.85f, 0.58f);
        [Range(0f, 1f), Tooltip("How strongly warm sources take the golden tint.")]
        public float bloomWarmth = 0.7f;
        [Range(0f, 1f), Tooltip("Colour isolation: 1 = only warm pixels bloom (sky/cold brights stop glowing entirely).")]
        public float bloomColorIsolation = 0.6f;

        [Header("Scene reference")]
        [Tooltip("URP post volume. Auto-found from StylizedSkyController or the scene when empty.")]
        public Volume postFxVolume;

        void OnEnable() { Active = this; ApplyAll(); }
        void OnDisable() { if (Active == this) Active = null; }
        void OnValidate() { if (isActiveAndEnabled) ApplyAll(); }

        [ContextMenu("Reapply")]
        public void ApplyAll()
        {
            if (postFxVolume == null)
            {
                var sky = FindFirstObjectByType<StylizedSkyController>();
                if (sky != null && sky.postFxVolume != null) postFxVolume = sky.postFxVolume;
                else postFxVolume = FindFirstObjectByType<Volume>();
            }
            var profile = postFxVolume != null ? postFxVolume.sharedProfile : null;
            if (profile == null) return;

            var tone = GetOrAdd<Tonemapping>(profile);
            tone.active = tonemapping != ToneMode.None;
            tone.mode.overrideState = true;
            tone.mode.value = tonemapping == ToneMode.ACES ? TonemappingMode.ACES : TonemappingMode.Neutral;

            var ca = GetOrAdd<ColorAdjustments>(profile);
            ca.active = true;
            Set(ca.postExposure, postExposure);
            Set(ca.contrast, contrast);
            Set(ca.saturation, saturation);
            Set(ca.hueShift, hueShift);
            ca.colorFilter.overrideState = true;
            ca.colorFilter.value = colorFilter;

            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.active = true;
            Set(wb.temperature, whiteBalanceTemperature);
            Set(wb.tint, whiteBalanceTint);

            var lgg = GetOrAdd<LiftGammaGain>(profile);
            lgg.active = true;
            SetV4(lgg.lift, liftColor, lift);
            SetV4(lgg.gamma, gammaColor, gamma);
            SetV4(lgg.gain, gainColor, gain);

            var smh = GetOrAdd<ShadowsMidtonesHighlights>(profile);
            smh.active = true;
            SetV4(smh.shadows, shadowsColor, shadowsOffset);
            SetV4(smh.midtones, midtonesColor, midtonesOffset);
            SetV4(smh.highlights, highlightsColor, highlightsOffset);

            // URP bloom is replaced by the custom warm bloom (URP's can only tint the whole glow globally).
            if (profile.TryGet(out Bloom urpBloom))
            {
                urpBloom.active = false;
                Set(urpBloom.intensity, 0f);
            }

            // The cozy brief keeps these off (taken over from StylizedSkyController).
            if (profile.TryGet(out Vignette vig)) vig.active = false;
            if (profile.TryGet(out ChromaticAberration cab)) cab.active = false;
            if (profile.TryGet(out FilmGrain grain)) grain.active = false;
            if (profile.TryGet(out LensDistortion dist)) dist.active = false;

#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(profile);
#endif
        }

        static void Set(FloatParameter p, float v) { p.overrideState = true; p.value = v; }

        static void SetV4(Vector4Parameter p, Color c, float w)
        {
            p.overrideState = true;
            p.value = new Vector4(c.r, c.g, c.b, w);
        }

        T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T comp)) return comp;
            comp = profile.Add<T>(true);
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.AssetDatabase.Contains(profile))
            {
                UnityEditor.AssetDatabase.AddObjectToAsset(comp, profile);
                UnityEditor.EditorUtility.SetDirty(profile);
            }
#endif
            return comp;
        }
    }
}
