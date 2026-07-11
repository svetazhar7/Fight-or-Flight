using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IslandSystem.Sun
{
    /// <summary>
    /// Art-directed shadows for the sun light. Per-light settings (strength, softness, near plane, bias) plus the
    /// URP asset settings (distance, resolution, cascades, splits) so everything lives in one inspector. The shadow
    /// lift guarantees shadows never crush to black: it raises the ambient floor after the environment module has
    /// written it (Order 40 > 30). Contact shadow strength drives the SSAO renderer feature's intensity when one
    /// exists. NOTE: distance/resolution/cascades edit the shared URP asset — they affect every scene using it.
    /// </summary>
    [DisallowMultipleComponent]
    public class SunShadowModule : SunModuleBase
    {
        public override int Order => 40;

        public enum ShadowRes { _1024 = 1024, _2048 = 2048, _4096 = 4096, _8192 = 8192 }

        [Header("Light shadows")]
        public bool shadowsEnabled = true;
        [Range(0f, 1f), Tooltip("Shadow darkness. Below 1 keeps them airy — the cozy look lives around 0.6–0.8.")]
        public float strength = 0.72f;
        public bool softShadows = true;
        [Range(0f, 3f), Tooltip("Extra softness near the horizon: low sun = softer, longer, dreamier shadows.")]
        public float horizonSoftening = 1f;
        [Range(0f, 10f)] public float nearPlane = 2f;
        [Range(0f, 3f)] public float depthBias = 1f;
        [Range(0f, 3f)] public float normalBias = 0.6f;

        [Header("URP asset (shared across scenes!)")]
        [Range(20f, 1000f)] public float shadowDistance = 250f;
        public ShadowRes resolution = ShadowRes._4096;
        [Range(1, 4)] public int cascadeCount = 4;
        [Tooltip("Cascade split fractions (used when cascade count is 4).")]
        public Vector3 cascadeSplits = new Vector3(0.067f, 0.2f, 0.467f);

        [Header("Contact & floor")]
        [Range(0f, 1f), Tooltip("Drives the SSAO feature intensity — grounding contact darkening under props/foliage.")]
        public float contactShadowStrength = 0.15f;
        [Range(0f, 0.5f), Tooltip("Minimum ambient luminance so shadows NEVER go fully black.")]
        public float shadowLift = 0.08f;

        public override void Apply(in SunContext ctx)
        {
            if (ctx.light != null)
            {
                float soft = 1f - ctx.horizonFactor * 0.35f * horizonSoftening;   // lower sun = weaker (softer-looking) shadow
                ctx.light.shadows = !shadowsEnabled ? LightShadows.None : (softShadows ? LightShadows.Soft : LightShadows.Hard);
                ctx.light.shadowStrength = Mathf.Clamp01(strength * soft);
                ctx.light.shadowNearPlane = nearPlane;
                ctx.light.shadowBias = depthBias;
                ctx.light.shadowNormalBias = normalBias;
            }

            var asset = UniversalRenderPipeline.asset;
            if (asset != null)
            {
                asset.shadowDistance = shadowDistance;
                asset.shadowCascadeCount = cascadeCount;
                asset.mainLightShadowmapResolution = (int)resolution;
                if (cascadeCount == 4) asset.cascade4Split = cascadeSplits;
                else if (cascadeCount == 3) asset.cascade3Split = new Vector2(cascadeSplits.x, cascadeSplits.y);
                else if (cascadeCount == 2) asset.cascade2Split = cascadeSplits.x;
            }

            ApplyContactShadows();
            ApplyShadowLift();
        }

        /// <summary>SSAO intensity via reflection — the settings type is internal to URP.</summary>
        void ApplyContactShadows()
        {
#if UNITY_EDITOR
            var asset = UniversalRenderPipeline.asset;
            if (asset == null) return;
            var rendererDataList = typeof(UniversalRenderPipelineAsset)
                .GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(asset) as ScriptableRendererData[];
            if (rendererDataList == null) return;
            foreach (var rd in rendererDataList)
            {
                if (rd == null) continue;
                foreach (var feature in rd.rendererFeatures)
                {
                    if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    var settingsField = feature.GetType().GetField("m_Settings",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var settings = settingsField?.GetValue(feature);
                    var intensityField = settings?.GetType().GetField("Intensity");
                    if (intensityField != null)
                    {
                        intensityField.SetValue(settings, contactShadowStrength * 2f);
                        UnityEditor.EditorUtility.SetDirty(rd);
                    }
                }
            }
#endif
        }

        /// <summary>Never-black shadows: raise the ambient floor written by the environment module (we run after it).</summary>
        void ApplyShadowLift()
        {
            Color amb = RenderSettings.ambientLight;
            float lum = 0.2126f * amb.r + 0.7152f * amb.g + 0.0722f * amb.b;
            if (lum < shadowLift)
            {
                float add = shadowLift - lum;
                RenderSettings.ambientLight = amb + new Color(add, add, add, 0f);
            }
        }
    }
}
