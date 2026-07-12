using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IslandSystem.Sun
{
    /// <summary>
    /// Couples the sun to the cloud field, in both directions.
    ///
    /// SUN OCCLUSION: reads IslandClouds' camera→sun puff test (smoothed) plus the overall coverage, turns it into
    /// per-channel visibility values with independent fade speeds, and the whole stack reacts through the
    /// SunContext: direct light dims toward a diffuse floor, god rays vanish completely, disc/halo/bloom feed
    /// melts, shadows soften toward overcast, ambient gets compensated so a covered sun reads as a bright soft
    /// grey day — never a dimmed one, and never a scene lit as if the cloud wasn't there.
    ///
    /// CLOUD SHADOWS: bakes the actual IslandClouds puff spheres into a top-down light COOKIE on the sun light —
    /// soft translucent blobs that match the clouds overhead and drift with the same wind. Rebakes automatically
    /// when the cloud layout changes (PuffVersion); the drift itself is just a cookie offset per frame, zero cost.
    ///
    /// Per-frame smoothing/drift runs in Update (play mode); everything else applies through OnValidate as usual.
    /// </summary>
    [DisallowMultipleComponent]
    public class SunCloudShadowModule : SunModuleBase
    {
        public override int Order => 5;   // runs first — other modules consume its output via the context

        public enum CookieRes { _128 = 128, _256 = 256, _512 = 512 }

        [Header("Scene reference")]
        [Tooltip("The cloud field. Auto-found when empty.")]
        public IslandClouds clouds;

        [Header("Sun occlusion (cloud covering the sun)")]
        [Range(0f, 1f), Tooltip("How strongly a cloud between the camera and the sun dims the whole sun stack.")]
        public float sunOcclusionStrength = 1f;
        [Range(0f, 1f), Tooltip("How much overall cloud COVERAGE dims the sun even without direct occlusion (overcast sky).")]
        public float coverageOvercast = 0.55f;
        [Range(0.05f, 5f), Tooltip("Fade speed of the direct light / shadows (per second).")]
        public float sunFadeSpeed = 0.8f;
        [Range(0.05f, 5f), Tooltip("Fade speed of the god rays — slightly faster feels right.")]
        public float rayFadeSpeed = 1.4f;
        [Range(0.05f, 5f), Tooltip("Fade speed of the halo / disc glow / bloom feed.")]
        public float glowFadeSpeed = 1f;

        [Header("Light response when covered")]
        [Range(0f, 1f), Tooltip("Direct light intensity floor when the sun is fully covered (diffuse light through the cloud).")]
        public float diffuseFloor = 0.3f;
        [Range(0f, 1f), Tooltip("Disc/halo/bloom floor when fully covered.")]
        public float glowFloor = 0.12f;
        [Range(0f, 1f), Tooltip("Shadow strength floor when fully covered (overcast = faint soft shadows).")]
        public float shadowFloor = 0.25f;
        [Range(0f, 1f), Tooltip("Ambient boost when covered — the light the clouds absorb comes back as soft sky light.")]
        public float ambientCompensation = 0.45f;

        [Header("Cloud shadows (light cookie on the sun)")]
        public bool cloudShadowsEnabled = true;
        [Range(0f, 1f), Tooltip("How dark the ground gets under a cloud blob.")]
        public float cloudShadowIntensity = 0.65f;
        [Range(0f, 1f), Tooltip("Master opacity of the whole cloud-shadow layer.")]
        public float cloudShadowOpacity = 1f;
        [Range(0f, 1f), Tooltip("Softness of each blob's falloff (0 = puffy discs, 1 = misty smudges).")]
        public float cloudShadowSoftness = 0.55f;
        [Range(0, 4), Tooltip("Extra blur passes over the baked shadow map.")]
        public int cloudShadowBlur = 1;
        [Tooltip("Tint of the cloud shadow (multiplied into the light). Keep it bright — clouds are translucent.")]
        public Color cloudShadowColor = new Color(0.62f, 0.68f, 0.8f);
        [Range(0f, 1f), Tooltip("Saturation of the tint (0 = neutral grey shadows).")]
        public float cloudShadowSaturation = 0.35f;
        [Range(0f, 3f), Tooltip("Drift speed multiplier relative to the visual cloud wind.")]
        public float cloudShadowSpeed = 1f;
        [Range(0.25f, 4f), Tooltip("World-size multiplier of the shadow field relative to the cloud field.")]
        public float cloudShadowScale = 1f;
        [Range(0f, 2f), Tooltip("Per-puff density accumulation — thick layered clouds cast heavier shadows.")]
        public float cloudShadowDensity = 1f;
        [Range(0f, 1f), Tooltip("Scale shadow opacity by sun height (grazing sun = long faint cloud shadows).")]
        public float sunHeightInfluence = 0.5f;
        public CookieRes cookieResolution = CookieRes._256;

        // ---- Values consumed by SunSystem when building the context ----
        public float SunVisibility => _visMain;
        public float DiffuseFade => Mathf.Lerp(diffuseFloor, 1f, _visMain);
        public float GlowFade => Mathf.Lerp(glowFloor, 1f, _visGlow);
        public float RayFade => _visRay * _visRay;   // quadratic: rays die out completely under cover
        public float ShadowFade => Mathf.Lerp(shadowFloor, 1f, _visMain);
        public float AmbientBoost => (1f - _visMain) * ambientCompensation;

        float _visMain = 1f, _visRay = 1f, _visGlow = 1f;
        Texture2D _cookie;
        int _bakedVersion = -1;
        float _bakedWorldSize;
        Quaternion _bakedLightRot;
        Light _cookieLight;

        protected override void OnEnable()
        {
            if (clouds == null) clouds = FindFirstObjectByType<IslandClouds>();
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            if (clouds != null) clouds.externalOcclusionDriver = false;
            if (_cookieLight != null) _cookieLight.cookie = null;
            base.OnDisable();
        }

        void Update()
        {
            if (clouds == null) return;

            // Smooth per-channel visibility toward the current occlusion target.
            float occ = clouds.SunOcclusion01 * sunOcclusionStrength;
            float overcast = Mathf.Clamp01(clouds.coverage) * coverageOvercast;
            float target = Mathf.Clamp01(1f - Mathf.Max(occ, overcast));
            float dt = Application.isPlaying ? Time.deltaTime : 1000f;   // edit mode: snap
            float m0 = _visMain, r0 = _visRay, g0 = _visGlow;
            _visMain = Mathf.MoveTowards(_visMain, target, sunFadeSpeed * dt);
            _visRay = Mathf.MoveTowards(_visRay, target, rayFadeSpeed * dt);
            _visGlow = Mathf.MoveTowards(_visGlow, target, glowFadeSpeed * dt);

            bool changed = Mathf.Abs(_visMain - m0) + Mathf.Abs(_visRay - r0) + Mathf.Abs(_visGlow - g0) > 0.0005f;
            if (changed) RequestApply();                       // re-run the whole stack with the new visibility

            bool rebake = clouds.PuffVersion != _bakedVersion
                || (_cookieLight != null && Quaternion.Angle(_bakedLightRot, _cookieLight.transform.rotation) > 2f);
            if (rebake) RequestApply();                                // layout/sun moved → rebake via Apply
            else if (Application.isPlaying) UpdateCookieDrift();       // cheap: offset only
        }

        public override void Apply(in SunContext ctx)
        {
            if (clouds == null) clouds = FindFirstObjectByType<IslandClouds>();
            if (clouds != null) clouds.externalOcclusionDriver = true;   // we own the sun dimming now

            _cookieLight = ctx.light;
            if (_cookieLight == null) return;

            if (!cloudShadowsEnabled || clouds == null || cloudShadowIntensity * cloudShadowOpacity <= 0.001f)
            {
                _cookieLight.cookie = null;
                return;
            }

            bool stale = clouds.PuffVersion != _bakedVersion || _cookie == null
                || Quaternion.Angle(_bakedLightRot, _cookieLight.transform.rotation) > 2f;
            if (stale) BakeCookie(ctx);
            _cookieLight.cookie = _cookie;

            var extra = _cookieLight.GetComponent<UniversalAdditionalLightData>();
            if (extra == null) extra = _cookieLight.gameObject.AddComponent<UniversalAdditionalLightData>();
            extra.lightCookieSize = new Vector2(_bakedWorldSize, _bakedWorldSize);
            UpdateCookieDrift();
        }

        /// <summary>Cookie offset = cloud field centre + wind drift, projected into the light's cross-section plane.</summary>
        void UpdateCookieDrift()
        {
            if (_cookieLight == null || _cookieLight.cookie == null || clouds == null) return;
            var extra = _cookieLight.GetComponent<UniversalAdditionalLightData>();
            if (extra == null) return;
            Vector3 world = clouds.transform.position + clouds.CurrentWindOffset * cloudShadowSpeed;
            Transform lt = _cookieLight.transform;
            extra.lightCookieOffset = new Vector2(Vector3.Dot(world, lt.right), Vector3.Dot(world, lt.up));
        }

        /// <summary>
        /// Draw every puff sphere as a soft blob into a top-down shadow map. CPU, ~1k puffs on a 256² target —
        /// runs only when the cloud layout actually changes, never per frame.
        /// </summary>
        void BakeCookie(in SunContext ctx)
        {
            int res = (int)cookieResolution;
            float worldSize = 2f * (clouds.areaRadius + clouds.cloudSize) * cloudShadowScale;
            if (worldSize < 1f) return;

            var alpha = new float[res * res];
            float falloffPow = Mathf.Lerp(3f, 1.2f, cloudShadowSoftness);

            // Bake in the LIGHT's cross-section plane (right/up axes), matching how a directional cookie is
            // projected — cloud height then shifts the shadow along the ground exactly like the real sun would.
            Transform lt = _cookieLight != null ? _cookieLight.transform : null;
            Vector3 right = lt != null ? lt.right : Vector3.right;
            Vector3 up = lt != null ? lt.up : Vector3.forward;
            Vector3 center = clouds.transform.position;
            float uc = Vector3.Dot(center, right), vc = Vector3.Dot(center, up);

            var puffs = clouds.Puffs;
            for (int i = 0; i < puffs.Count; i++)
            {
                Vector4 p = puffs[i];
                Vector3 w = new Vector3(p.x, p.y, p.z);
                float px = (Vector3.Dot(w, right) - uc) / worldSize + 0.5f;
                float pz = (Vector3.Dot(w, up) - vc) / worldSize + 0.5f;
                float prN = p.w / worldSize;
                int minX = Mathf.Max(0, Mathf.FloorToInt((px - prN) * res));
                int maxX = Mathf.Min(res - 1, Mathf.CeilToInt((px + prN) * res));
                int minY = Mathf.Max(0, Mathf.FloorToInt((pz - prN) * res));
                int maxY = Mathf.Min(res - 1, Mathf.CeilToInt((pz + prN) * res));
                float rPix = Mathf.Max(1f, prN * res);
                float cxPix = px * res, cyPix = pz * res;
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = x + 0.5f - cxPix, dy = y + 0.5f - cyPix;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / rPix;
                        if (d >= 1f) continue;
                        alpha[y * res + x] += Mathf.Pow(1f - d, falloffPow) * cloudShadowDensity * 0.8f;
                    }
                }
            }

            for (int i = 0; i < cloudShadowBlur; i++) BoxBlur(alpha, res);

            // Tint: shadows head toward a bright translucent colour, never black.
            Color tint = SunColorUtil.AdjustSaturation(cloudShadowColor, cloudShadowSaturation);
            float heightFactor = Mathf.Lerp(1f, Mathf.Lerp(0.55f, 1f, ctx.elevation01), sunHeightInfluence);
            float master = Mathf.Clamp01(cloudShadowIntensity * cloudShadowOpacity * heightFactor);

            if (_cookie == null || _cookie.width != res)
            {
                // No mipmaps: a mip-averaged cookie tints the whole footprint slightly and draws a visible
                // rectangle around the field on flat water.
                _cookie = new Texture2D(res, res, TextureFormat.RGBA32, false)
                {
                    name = "SunCloudShadowCookie",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
            }
            var pixels = new Color32[res * res];
            for (int i = 0; i < alpha.Length; i++)
            {
                Color c = Color.Lerp(Color.white, tint, Mathf.Clamp01(alpha[i]) * master);
                pixels[i] = c;
            }
            // Clear border so the clamped cookie never smears a shadow to the horizon.
            for (int x = 0; x < res; x++) { pixels[x] = Color.white; pixels[(res - 1) * res + x] = Color.white; }
            for (int y = 0; y < res; y++) { pixels[y * res] = Color.white; pixels[y * res + res - 1] = Color.white; }

            _cookie.SetPixels32(pixels);
            _cookie.Apply(false, false);
            _bakedVersion = clouds.PuffVersion;
            _bakedWorldSize = worldSize;
            if (lt != null) _bakedLightRot = lt.rotation;
        }

        static void BoxBlur(float[] a, int res)
        {
            var tmp = (float[])a.Clone();
            for (int y = 1; y < res - 1; y++)
                for (int x = 1; x < res - 1; x++)
                {
                    int i = y * res + x;
                    a[i] = (tmp[i] * 4f + tmp[i - 1] + tmp[i + 1] + tmp[i - res] + tmp[i + res]) / 8f;
                }
        }
    }
}
