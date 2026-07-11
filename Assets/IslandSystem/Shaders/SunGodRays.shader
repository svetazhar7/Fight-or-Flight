Shader "Hidden/IslandSystem/SunGodRays"
{
    // Screen-space god rays, driven entirely by SunGodRaysFeature via _GR_* globals.
    // Pass 0: occlusion mask from the camera depth texture (bound as _BlitTexture) — sky around the sun's screen
    //         position, carved by depth-writing geometry, shaped by periodic angular streaks + animated noise.
    // Pass 1: radial blur toward the sun (ping-ponged by the feature for a taps² effective sample count).
    // Pass 2: additive composite (tint, brightness, opacity, screen-edge fade) before post-processing.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _GR_SunUV;      // x,y = sun viewport uv, z = aspect, w = master fade (edge + facing)
        float4 _GR_Mask;       // x = max distance, y = sharpness, z = edge softness, w = unused
        float4 _GR_Blur;       // x = density (uv length), y = decay, z = scattering weight, w = sample count
        float4 _GR_Noise;      // x = scale, y = strength, z = time (pre-multiplied by speed), w = unused
        float4 _GR_Streak;     // x = beam count (rounded), y = strength, z = sharpness, w = unused
        float4 _GR_Composite;  // x = brightness, y = intensity, z = opacity, w = unused
        float4 _GR_Texel;      // x,y = 1/rt size
        half4  _GR_Color;

        float GR_Hash(float n) { return frac(sin(n) * 43758.5453123); }

        float GR_Noise1D(float x)
        {
            float i = floor(x), f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(GR_Hash(i), GR_Hash(i + 1.0), f);
        }

        float GR_Noise2D(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = GR_Hash(dot(i, float2(127.1, 311.7)));
            float b = GR_Hash(dot(i + float2(1, 0), float2(127.1, 311.7)));
            float c = GR_Hash(dot(i + float2(0, 1), float2(127.1, 311.7)));
            float d = GR_Hash(dot(i + float2(1, 1), float2(127.1, 311.7)));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }
        ENDHLSL

        Pass
        {
            Name "SunGodRays Mask"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMask

            float SkyAt(float2 uv)
            {
                float raw = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).r;
                #if UNITY_REVERSED_Z
                    return raw < 1.0e-5 ? 1.0 : 0.0;
                #else
                    return raw > 0.99999 ? 1.0 : 0.0;
                #endif
            }

            half4 FragMask(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // Soft occlusion mask: a 5-tap box widened by edge softness melts the hard depth silhouette.
                float2 t = _GR_Texel.xy * (1.0 + _GR_Mask.z * 5.0);
                float sky = SkyAt(uv) * 0.4;
                sky += SkyAt(uv + float2( t.x, 0)) * 0.15;
                sky += SkyAt(uv + float2(-t.x, 0)) * 0.15;
                sky += SkyAt(uv + float2(0,  t.y)) * 0.15;
                sky += SkyAt(uv + float2(0, -t.y)) * 0.15;

                float2 d = uv - _GR_SunUV.xy;
                d.x *= _GR_SunUV.z;
                float dist = length(d);
                float radial = pow(saturate(1.0 - dist / _GR_Mask.x), _GR_Mask.y);

                float m = sky * radial;

                // Periodic angular beams (count rounded on CPU so sin stays seam-free) with a slow phase drift,
                // plus per-angle noise so the beams breathe instead of ticking like clockwork.
                if (_GR_Streak.x >= 1.0 && _GR_Streak.y > 0.001)
                {
                    float ang = atan2(d.y, d.x);
                    float phase = _GR_Noise.z * 0.4 + GR_Noise1D(ang * 2.0 + _GR_Noise.z * 0.25) * _GR_Noise.y * 3.0;
                    float beams = 0.5 + 0.5 * sin(ang * _GR_Streak.x + phase);
                    beams = pow(saturate(beams), _GR_Streak.z);
                    m *= lerp(1.0, beams, saturate(_GR_Streak.y));
                }

                return half4(m.xxx, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SunGodRays Radial"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragRadial

            half4 FragRadial(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                int samples = (int)_GR_Blur.w;
                float2 delta = (_GR_SunUV.xy - uv) * _GR_Blur.x / max(samples, 1);

                // Jittered start hides banding at low sample counts and lets the noise ripple down the shafts.
                float jitter = GR_Noise2D(uv * (10.0 + _GR_Noise.x * 8.0) + _GR_Noise.z) * _GR_Noise.y;
                float2 p = uv + delta * jitter;

                half3 acc = 0;
                float w = 1.0, total = 0.0;
                [loop]
                for (int s = 0; s < 64; s++)
                {
                    if (s >= samples) break;
                    acc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, p).rgb * w;
                    total += w;
                    w *= _GR_Blur.y;
                    p += delta;
                }
                acc /= max(total, 1.0e-4);
                return half4(acc * _GR_Blur.z, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SunGodRays Composite"
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            half4 FragComposite(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half3 rays = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // Slow whole-field shimmer — the "air is alive" touch, kept subtle.
                float breathe = 1.0 + (GR_Noise2D(uv * _GR_Noise.x + _GR_Noise.z * 0.3) - 0.5) * _GR_Noise.y * 0.5;

                half3 col = rays * _GR_Color.rgb * _GR_Composite.x * breathe;
                return half4(col * _GR_Composite.y * _GR_Composite.z * _GR_SunUV.w, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
