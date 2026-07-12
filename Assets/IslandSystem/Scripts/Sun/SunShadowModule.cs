using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IslandSystem.Sun
{
    /// <summary>
    /// The art-directed shadow system for the sun. One inspector drives four layers:
    ///   QUALITY — URP asset settings (resolution, distance, cascade count/splits/blend, filtering quality) with an
    ///     auto-cascade mode and quality PROFILES (Low..Cinematic) you can switch instantly;
    ///   STABILITY — the anti-flicker block: conservative enclosing-sphere cascades (kills the shadow shimmer on
    ///     tree crowns while walking/turning), tight shadow distance for texel density, cascade border blending so
    ///     cascade switches are invisible;
    ///   ARTISTIC — strength/opacity/contrast plus the shadow FILL tint: shadows are lifted from below by a tinted
    ///     ambient floor (never pure black) that can be neutral, cool, warm or synced to the live sun colour;
    ///   RESPONSE — automatic adaptation to the sun and the air: lower sun = longer, softer, weaker shadows; more
    ///     haze/fog = less contrast and softer edges; clouds over the sun = shadows fade toward a soft overcast.
    /// Contact shadows are the SSAO feature driven through the same inspector. Everything applies via OnValidate.
    /// NOTE ON MAPPING: URP has no per-pixel shadow recolour — tint/saturation/brightness act on the ambient fill
    /// that shadowed areas fall back to, which is the physically sane way to get "warm readable shadows".
    /// </summary>
    [DisallowMultipleComponent]
    public class SunShadowModule : SunModuleBase
    {
        public override int Order => 40;

        public enum ShadowRes { _1024 = 1024, _2048 = 2048, _4096 = 4096, _8192 = 8192 }
        public enum Profile { Low, Medium, High, Ultra, Cinematic, Custom }
        public enum TintMode { Neutral, Cool, Warm, SyncWithSun }
        public enum UpdateMode { Continuous, OnValidateOnly }

        [Header("Profile")]
        [Tooltip("Quality preset. Selecting one overwrites the QUALITY + STABILITY fields; Custom never overwrites.")]
        public Profile profile = Profile.High;

        [Header("Quality (URP asset — shared across scenes!)")]
        public ShadowRes resolution = ShadowRes._4096;
        [Range(20f, 1000f), Tooltip("Max shadow distance. SMALLER = more texels per meter = crisper, more stable shadows.")]
        public float shadowDistance = 180f;
        [Tooltip("Compute cascade splits automatically from the distance (near-weighted so trees around the player get the densest texels).")]
        public bool autoCascades = true;
        [Range(1, 4)] public int cascadeCount = 4;
        [Tooltip("Manual cascade split fractions (when Auto Cascades is off).")]
        public Vector3 cascadeSplits = new Vector3(0.06f, 0.18f, 0.42f);
        [Range(0f, 1f), Tooltip("Cascade border blend — cross-fades cascade transitions so you never see the switch line.")]
        public float cascadeBlend = 0.2f;
        [Tooltip("PCF filtering quality of the soft shadow edge.")]
        public SoftShadowQuality filteringQuality = SoftShadowQuality.High;

        [Header("Stability (anti-flicker)")]
        [Tooltip("Conservative enclosing sphere: cascades stop re-fitting as the camera moves/turns — THE fix for shimmering tree-crown shadows while walking.")]
        public bool stabilizeCascades = true;
        [Tooltip("Continuous: shadows keep adapting to sun height, haze and clouds. OnValidateOnly: fixed baseline, dynamic modifiers ignored.")]
        public UpdateMode updateMode = UpdateMode.Continuous;

        [Header("Bias (acne / peter-panning)")]
        [Range(0f, 3f)] public float depthBias = 1f;
        [Range(0f, 3f)] public float normalBias = 0.7f;
        [Range(0f, 10f)] public float nearPlane = 2f;

        [Header("Artistic")]
        [Range(0f, 1f), Tooltip("Shadow darkness before the modifiers below.")]
        public float strength = 0.85f;
        [Range(0f, 1f), Tooltip("Master opacity multiplied over strength.")]
        public float opacity = 1f;
        [Range(0.25f, 2f), Tooltip("Contrast of the shadow term around mid-grey (1 = neutral).")]
        public float contrast = 1.12f;
        public bool softShadows = true;
        [Range(0f, 2f), Tooltip("Penumbra feel: widens the filtered edge via normal bias (0 = tight, 2 = dreamy).")]
        public float penumbraSize = 0.8f;
        [Range(0f, 1f), Tooltip("Extra edge softness on top of the penumbra.")]
        public float edgeSoftness = 0.35f;

        [Header("Shadow fill tint (the never-black floor)")]
        public TintMode tintMode = TintMode.SyncWithSun;
        [Tooltip("Custom tint multiplied into the fill colour.")]
        public Color shadowColor = Color.white;
        [Range(-1f, 1f), Tooltip("Warm/cool fine-shift of the fill.")]
        public float temperature = 0f;
        [Range(0f, 1.5f)] public float saturation = 1.12f;
        [Range(0f, 0.5f), Tooltip("Minimum ambient luminance in shadow — shadows can NEVER crush to black.")]
        public float shadowLift = 0.07f;
        [Range(0f, 2f), Tooltip("Brightness of the lift floor.")]
        public float liftBrightness = 1f;

        [Header("Sun & atmosphere response")]
        [Range(0f, 3f), Tooltip("Low sun = softer, weaker shadows (and warmer via SyncWithSun).")]
        public float horizonSoftening = 1f;
        [Range(0f, 1f), Tooltip("Haze/fog reduce shadow contrast (thick air scatters light into shadows).")]
        public float hazeContrastReduction = 0.5f;
        [Range(0f, 1f), Tooltip("Haze/fog soften shadow edges.")]
        public float hazeEdgeSoften = 0.5f;

        [Header("Contact shadows (SSAO feature)")]
        [Range(0f, 1f), Tooltip("Grounding contact darkening under props/foliage. 0 disables.")]
        public float contactStrength = 0.15f;
        [Range(0.05f, 2f), Tooltip("Contact shadow reach (SSAO radius, meters).")]
        public float contactDistance = 0.35f;
        [Range(0f, 300f), Tooltip("Distance where contact shadows fade out (SSAO falloff).")]
        public float contactFadeDistance = 120f;
        [Range(0f, 1f), Tooltip("How much direct light suppresses contact shadows (keeps lit areas clean).")]
        public float contactLightingInfluence = 0.6f;
        [Range(0f, 1f), Tooltip("Scale contact strength by sun height (grazing light = weaker contact).")]
        public float contactSunHeightInfluence = 0.5f;

        [SerializeField, HideInInspector] Profile _loadedProfile = (Profile)(-1);

        public override void Apply(in SunContext ctx)
        {
            if (profile != Profile.Custom && _loadedProfile != profile) LoadProfile(profile);

            bool dynamic = updateMode == UpdateMode.Continuous;
            float haze = dynamic ? Mathf.Clamp01((ctx.atmosphericDensity - 1f) * 0.5f + RenderSettings.fogDensity * 400f) : 0f;

            // ---- Per-light ----
            if (ctx.light != null)
            {
                float s = strength * opacity;
                s = Mathf.Clamp01((s - 0.5f) * contrast + 0.5f);                                   // artistic contrast
                if (dynamic)
                {
                    s *= 1f - ctx.horizonFactor * 0.35f * Mathf.Clamp01(horizonSoftening * 0.5f);  // low sun = airier
                    s *= 1f - haze * hazeContrastReduction * 0.5f;                                 // thick air = flatter
                    s *= ctx.cloudShadowFade;                                                      // overcast = faint
                }
                ctx.light.shadows = s <= 0.001f ? LightShadows.None : (softShadows ? LightShadows.Soft : LightShadows.Hard);
                ctx.light.shadowStrength = Mathf.Clamp01(s);
                ctx.light.shadowNearPlane = nearPlane;
                ctx.light.shadowBias = depthBias;
                float nb = normalBias * (1f + penumbraSize * 0.5f + edgeSoftness * 0.5f);
                if (dynamic) nb *= 1f + haze * hazeEdgeSoften * 0.5f + ctx.horizonFactor * 0.25f * horizonSoftening;
                ctx.light.shadowNormalBias = Mathf.Clamp(nb, 0f, 3f);

                var extra = ctx.light.GetComponent<UniversalAdditionalLightData>();
                if (extra == null) extra = ctx.light.gameObject.AddComponent<UniversalAdditionalLightData>();
                extra.softShadowQuality = filteringQuality;
            }

            // ---- URP asset ----
            var asset = UniversalRenderPipeline.asset;
            if (asset != null)
            {
                asset.shadowDistance = shadowDistance;
                asset.shadowCascadeCount = cascadeCount;
                asset.mainLightShadowmapResolution = (int)resolution;
                asset.cascadeBorder = cascadeBlend;
                asset.conservativeEnclosingSphere = stabilizeCascades;
                Vector3 splits = autoCascades ? AutoSplits() : cascadeSplits;
                if (cascadeCount == 4) asset.cascade4Split = splits;
                else if (cascadeCount == 3) asset.cascade3Split = new Vector2(splits.x, splits.y);
                else if (cascadeCount == 2) asset.cascade2Split = splits.x;
            }

            ApplyContactShadows(ctx);
            ApplyShadowLift(ctx);
        }

        /// <summary>Near-weighted splits: cascade 0 hugs the player so foliage nearby gets the densest texels.</summary>
        Vector3 AutoSplits()
        {
            switch (cascadeCount)
            {
                case 4: return new Vector3(0.05f, 0.16f, 0.4f);
                case 3: return new Vector3(0.08f, 0.3f, 0f);
                default: return new Vector3(0.2f, 0f, 0f);
            }
        }

        /// <summary>SSAO = our contact shadows. Settings type is internal to URP, hence reflection (editor only).</summary>
        void ApplyContactShadows(in SunContext ctx)
        {
#if UNITY_EDITOR
            var asset = UniversalRenderPipeline.asset;
            if (asset == null) return;
            var rendererDataList = typeof(UniversalRenderPipelineAsset)
                .GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(asset) as ScriptableRendererData[];
            if (rendererDataList == null) return;

            float sunFactor = Mathf.Lerp(1f, Mathf.Lerp(0.4f, 1f, ctx.elevation01), contactSunHeightInfluence);
            foreach (var rd in rendererDataList)
            {
                if (rd == null) continue;
                foreach (var feature in rd.rendererFeatures)
                {
                    if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    var settings = feature.GetType()
                        .GetField("m_Settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.GetValue(feature);
                    if (settings == null) continue;
                    var t = settings.GetType();
                    t.GetField("Intensity")?.SetValue(settings, contactStrength * 2f * sunFactor);
                    t.GetField("Radius")?.SetValue(settings, contactDistance);
                    t.GetField("Falloff")?.SetValue(settings, contactFadeDistance);
                    t.GetField("DirectLightingStrength")?.SetValue(settings, contactLightingInfluence);
                    feature.SetActive(contactStrength > 0.001f);
                    UnityEditor.EditorUtility.SetDirty(rd);
                }
            }
#endif
        }

        /// <summary>
        /// The never-black guarantee, tinted. Runs after SunEnvironmentModule (Order 30 &lt; 40): if the ambient the
        /// environment produced is darker than the lift floor, raise it with the FILL colour — neutral, cool, warm
        /// or the live sun colour, warmed further as the sun drops (warm sun = warm shadows, automatically).
        /// </summary>
        void ApplyShadowLift(in SunContext ctx)
        {
            Color fill;
            switch (tintMode)
            {
                case TintMode.Cool: fill = new Color(0.78f, 0.86f, 1f); break;
                case TintMode.Warm: fill = new Color(1f, 0.89f, 0.78f); break;
                case TintMode.SyncWithSun: fill = Color.Lerp(Color.white, ctx.sunColor, 0.55f); break;
                default: fill = Color.white; break;
            }
            fill *= shadowColor;
            fill = SunColorUtil.ShiftTemperature(fill, temperature);
            fill = SunColorUtil.AdjustSaturation(fill, saturation);

            Color amb = RenderSettings.ambientLight;
            float lum = 0.2126f * amb.r + 0.7152f * amb.g + 0.0722f * amb.b;
            float floor = shadowLift * liftBrightness;
            if (lum < floor)
            {
                float fillLum = Mathf.Max(1e-4f, 0.2126f * fill.r + 0.7152f * fill.g + 0.0722f * fill.b);
                RenderSettings.ambientLight = amb + fill * ((floor - lum) / fillLum);
            }
        }

        void LoadProfile(Profile p)
        {
            switch (p)
            {
                case Profile.Low:
                    resolution = ShadowRes._1024; shadowDistance = 100f; cascadeCount = 2; cascadeBlend = 0.1f;
                    filteringQuality = SoftShadowQuality.Low; stabilizeCascades = true; autoCascades = true;
                    contactStrength = 0f;
                    break;
                case Profile.Medium:
                    resolution = ShadowRes._2048; shadowDistance = 150f; cascadeCount = 3; cascadeBlend = 0.15f;
                    filteringQuality = SoftShadowQuality.Medium; stabilizeCascades = true; autoCascades = true;
                    contactStrength = 0.1f;
                    break;
                case Profile.High:
                    // 8192 / 150 m = ~2x the texels-per-metre of the old 4096 / 180. Denser texels are the direct
                    // cure for sub-texel SWIM on alpha-clipped crowns (smaller texel = smaller per-frame snap step),
                    // and the tighter distance both sharpens near shadows and bounds the stable tree caster set.
                    resolution = ShadowRes._8192; shadowDistance = 150f; cascadeCount = 4; cascadeBlend = 0.25f;
                    filteringQuality = SoftShadowQuality.High; stabilizeCascades = true; autoCascades = true;
                    contactStrength = 0.2f;
                    break;
                case Profile.Ultra:
                    resolution = ShadowRes._8192; shadowDistance = 300f; cascadeCount = 4; cascadeBlend = 0.25f;
                    filteringQuality = SoftShadowQuality.High; stabilizeCascades = true; autoCascades = true;
                    contactStrength = 0.2f;
                    break;
                case Profile.Cinematic:
                    resolution = ShadowRes._8192; shadowDistance = 160f; cascadeCount = 4; cascadeBlend = 0.3f;
                    filteringQuality = SoftShadowQuality.High; stabilizeCascades = true; autoCascades = true;
                    contactStrength = 0.25f; penumbraSize = 1.2f; edgeSoftness = 0.5f;
                    break;
            }
            _loadedProfile = p;
        }
    }
}
