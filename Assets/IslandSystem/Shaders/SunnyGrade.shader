Shader "Hidden/IslandSystem/SunnyGrade"
{
    // Selective "warm summer day" grading + custom warm bloom, driven by SunnyGradeFeature via _SG_* globals.
    // The whole point vs stock URP grading: WARMTH IS HUE-SELECTIVE. Warm ops (golden highlights, warm-only
    // saturation, golden bloom tint) are weighted by a per-pixel hue warmth mask and gated by blue/sky protection
    // masks, so the blue sky and cold materials keep their clean hue while sunlit grass/foliage/surfaces glow.
    // Pass 0: bloom prefilter (quadratic knee threshold, warm colour isolation, per-source golden tint) → half res.
    // Pass 1: 8-tap golden-angle disc blur (ping-ponged, radius grows per iteration).
    // Pass 2: grade + bloom composite (reads camera colour as _BlitTexture + _SG_BloomTex global) → full-res temp.
    // Pass 3: copy back to the camera target.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_SG_BloomTex);

        float4 _SG_BloomA;     // x threshold, y warmth, z isolation, w intensity
        float4 _SG_BloomB;     // x radius (texels), y unused, z,w texel size of the blur RT
        half4  _SG_BloomTint;  // golden bloom colour
        float4 _SG_GradeA;     // x exposure, y highlightWarmth*warmStrength, z shadowCoolness, w warmSaturation
        float4 _SG_GradeB;     // x bluePreservation, y skyProtection, z coldPreservation, w shadowLift
        float4 _SG_GradeC;     // x localContrast, y highlightCompression, z master strength, w unused
        half4  _SG_WarmColor;  // golden highlight tint
        half4  _SG_CoolColor;  // cool shadow tint
        half4  _SG_LiftColor;  // shadow lift fill colour
        float4 _SG_Texel;      // x,y = 1/full-res size
        float4 _SG_HSL[8];     // per Lightroom band: x hue shift, y saturation, z luminance (all -1..1)

        // Hue centres of the 8 Lightroom bands (red..magenta) on the 0..1 wheel.
        static const float SG_BandCenter[8] = { 0.0, 0.055, 0.15, 0.33, 0.5, 0.62, 0.75, 0.88 };

        float SG_Luma(half3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

        // Full RGB<->HSV (value may exceed 1 for HDR — hue/sat stay valid). Standard branchless form.
        float3 SG_RGB2HSV(float3 c)
        {
            float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
            float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
            float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
            float d = q.x - min(q.w, q.y);
            float e = 1.0e-10;
            return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
        }
        float3 SG_HSV2RGB(float3 c)
        {
            float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
            float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
            return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
        }
        float SG_HueDist(float a, float b) { float d = abs(a - b); return min(d, 1.0 - d); }

        // Hue in 0..1 (same wheel as Color.RGBToHSV) + saturation, scale-invariant so HDR intensity doesn't matter.
        float2 SG_HueSat(half3 c)
        {
            float mx = max(c.r, max(c.g, c.b));
            float mn = min(c.r, min(c.g, c.b));
            float d = mx - mn;
            float sat = mx > 1e-4 ? d / mx : 0.0;
            if (d < 1e-5) return float2(0.0, 0.0);
            float h;
            if (mx == c.r)      h = (c.g - c.b) / d;
            else if (mx == c.g) h = 2.0 + (c.b - c.r) / d;
            else                h = 4.0 + (c.r - c.g) / d;
            h = frac(h / 6.0 + 1.0);
            return float2(h, sat);
        }

        // 1 on red→yellow→yellow-green hues, 0 through cyan/blue, back to 1 at magenta-red. Grass (~0.25) is
        // partially warm on purpose — sunlit greens should take the golden light without going acid.
        float SG_HueWarmth(float h)
        {
            return saturate(1.0 - smoothstep(0.22, 0.42, h) + smoothstep(0.78, 0.92, h));
        }

        // Cyan→blue window, scaled up by saturation later where it matters.
        float SG_Blueness(float h)
        {
            return smoothstep(0.42, 0.50, h) * (1.0 - smoothstep(0.72, 0.80, h));
        }
        ENDHLSL

        Pass
        {
            Name "SunnyGrade Prefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter

            half4 FragPrefilter(Varyings input) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;

                // Quadratic-knee bright extract (standard soft threshold — no hard bloom popping).
                float br = max(c.r, max(c.g, c.b));
                float knee = _SG_BloomA.x * 0.5 + 1e-4;
                float soft = clamp(br - _SG_BloomA.x + knee, 0.0, 2.0 * knee);
                soft = soft * soft / (4.0 * knee);
                float contrib = max(soft, br - _SG_BloomA.x) / max(br, 1e-4);
                half3 b = c * contrib;

                // Colour isolation: cold sources contribute less bloom; golden tint only on warm sources, so the
                // bloom reads as sunlit air around bright grass/foliage, never a yellow film over the blue sky.
                float2 hs = SG_HueSat(c);
                float warmM = lerp(1.0, SG_HueWarmth(hs.x), saturate(hs.y * 4.0)); // grey-ish brights count as warm sun
                b *= lerp(1.0, warmM, _SG_BloomA.z);
                b *= lerp(half3(1, 1, 1), _SG_BloomTint.rgb, _SG_BloomA.y * warmM);
                return half4(b, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SunnyGrade Blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur

            half4 FragBlur(Varyings input) : SV_Target
            {
                // 8-tap golden-angle disc — cheap, isotropic, and ping-ponging it grows a smooth airy halo.
                const float2 taps[8] = {
                    float2( 1.000,  0.000), float2(-0.737,  0.676),
                    float2( 0.086, -0.996), float2( 0.610,  0.792),
                    float2(-0.987, -0.160), float2( 0.803, -0.596),
                    float2(-0.220,  0.976), float2(-0.559, -0.829)
                };
                float2 r = _SG_BloomB.zw * _SG_BloomB.x;
                half3 acc = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb * 0.2;
                [unroll]
                for (int i = 0; i < 8; i++)
                    acc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + taps[i] * r).rgb * 0.1;
                return half4(acc, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SunnyGrade Grade"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGrade

            half4 FragGrade(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half3 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 c = src * _SG_GradeA.x;   // exposure (pre-grade, linear HDR)

                float L = SG_Luma(c);
                float ln = L / (1.0 + L);       // tonemapped-ish luma proxy: stable masks in HDR
                float2 hs = SG_HueSat(c);
                float warmth = SG_HueWarmth(hs.x);
                float blue = SG_Blueness(hs.x) * smoothstep(0.06, 0.25, hs.y);   // grey isn't "blue"
                float skyM = SG_Blueness(hs.x) * smoothstep(0.25, 0.5, ln);      // bright blue = sky/water

                float shadowM = 1.0 - smoothstep(0.05, 0.35, ln);
                float highM = smoothstep(0.35, 0.75, ln);

                // Cold pixels opt out of every warm op (dialable): blue jeans stay blue, sky stays sky.
                float protect = saturate(1.0 - blue * _SG_GradeB.x - skyM * _SG_GradeB.y);
                float coldScale = 1.0 - (1.0 - warmth) * _SG_GradeB.z;

                // Shadow lift: tinted fill floor so darks keep detail and never crush to black.
                c += _SG_LiftColor.rgb * (_SG_GradeB.w * shadowM);

                // Golden highlights (the "air full of light") — masked by blue/sky protection + cold preservation.
                float hw = _SG_GradeA.y * highM * protect * coldScale;
                c *= lerp(half3(1, 1, 1), _SG_WarmColor.rgb, hw);

                // Cool shadows: natural warm-light/cool-shade colour contrast. Gentle, never deep blue.
                c *= lerp(half3(1, 1, 1), _SG_CoolColor.rgb, _SG_GradeA.z * shadowM);

                // Warm-only saturation: juicy sunlit greens/yellows; cold hues keep their authored purity.
                L = SG_Luma(c);
                c = lerp(L.xxx, c, 1.0 + _SG_GradeA.w * warmth);

                // Per-colour HSL (Lightroom-style): each of 8 hue bands shifts its own hue / saturation / luminance,
                // hue-weighted so a band only touches its own colours (greens can be juiced without warming the sky).
                {
                    float3 hsv = SG_RGB2HSV(max(c, 0.0));
                    float dh = 0.0, ds = 0.0, dl = 0.0;
                    [unroll]
                    for (int k = 0; k < 8; k++)
                    {
                        float w = saturate(1.0 - SG_HueDist(hsv.x, SG_BandCenter[k]) / 0.10);
                        w *= w;
                        dh += _SG_HSL[k].x * w;
                        ds += _SG_HSL[k].y * w;
                        dl += _SG_HSL[k].z * w;
                    }
                    hsv.x = frac(hsv.x + dh * 0.1);              // hue shift up to ~±36°
                    hsv.y = saturate(hsv.y * (1.0 + ds));         // saturation ±100%
                    hsv.z = max(0.0, hsv.z * (1.0 + dl * 0.5));   // luminance ±50%
                    c = SG_HSV2RGB(hsv);
                }

                // Local contrast: unsharp mask on luma against a 4-tap cross blur — volume without harshness.
                if (_SG_GradeC.x > 0.001)
                {
                    float2 t = _SG_Texel.xy * 2.5;
                    float Lb = SG_Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( t.x, 0)).rgb)
                             + SG_Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x, 0)).rgb)
                             + SG_Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0,  t.y)).rgb)
                             + SG_Luma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, -t.y)).rgb);
                    Lb = Lb * 0.25 * _SG_GradeA.x;
                    float lc = clamp(1.0 + _SG_GradeC.x * (L - Lb) / (L + Lb + 1e-3), 0.7, 1.4);
                    c *= lc;
                }

                // Warm bloom on top of the graded scene — light added to the air, not a screen-space veil.
                half3 bloom = SAMPLE_TEXTURE2D_X(_SG_BloomTex, sampler_LinearClamp, uv).rgb;
                c += bloom * _SG_BloomA.w;

                // Soft highlight roll-off ABOVE white so nothing clips before the tonemapper (keeps sun details).
                float Lf = SG_Luma(c);
                if (_SG_GradeC.y > 0.001 && Lf > 1.0)
                {
                    float compressed = 1.0 + (Lf - 1.0) / (1.0 + _SG_GradeC.y * (Lf - 1.0));
                    c *= compressed / Lf;
                }

                return half4(lerp(src, c, _SG_GradeC.z), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SunnyGrade Copy"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCopy

            half4 FragCopy(Varyings input) : SV_Target
            {
                return half4(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
