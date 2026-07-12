Shader "Hidden/IslandSystem/SunVolumetricLight"
{
    // TRUE volumetric light shafts: ray-march the camera ray and sample the MAIN LIGHT SHADOW MAP each step,
    // accumulating in-scattered sunlight in the lit air. Unlike the screen-space god rays (a glow around the sun's
    // screen position), this shows shafts landing on a glade / streaming between foliage from ANY view and ANY sun
    // elevation — and it is occluded for free: wherever a tree or cloud (cloud-shadow cookie is baked into the sun
    // shadows) shadows the air, that column of the shaft goes dark.
    // Pass 0: march (half res) → scatter. Pass 1: depth-aware-ish box blur. Pass 2: additive composite.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4x4 _VL_InvVP;      // inverse view-projection (GPU) for world-pos reconstruction
        float4   _VL_Params;     // x steps, y maxDist, z density, w heightFalloff
        float4   _VL_Params2;    // x isoFloor, y anisotropy(g), z groundY, w intensity*cloudRayFade
        float4   _VL_SunDir;     // xyz = direction TO the sun (world), w = master fade
        half4    _VL_Color;      // sun tint of the shafts
        float4   _VL_Texel;      // x,y = 1/half-res

        // Henyey-Greenstein phase, normalized so g=0 gives 1.
        float VL_HG(float cosT, float g)
        {
            float g2 = g * g;
            float denom = 1.0 + g2 - 2.0 * g * cosT;
            return (1.0 - g2) / max(1e-4, pow(abs(denom), 1.5));
        }

        float3 VL_WorldPos(float2 uv, float rawDepth)
        {
            float4 clip = float4(uv * 2.0 - 1.0, rawDepth, 1.0);
            #if UNITY_UV_STARTS_AT_TOP
                clip.y = -clip.y;
            #endif
            float4 w = mul(_VL_InvVP, clip);
            return w.xyz / w.w;
        }
        ENDHLSL

        Pass
        {
            Name "Volumetric March"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMarch
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            float VL_Shadow(float3 p)
            {
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 sc = TransformWorldToShadowCoord(p);
                return MainLightRealtimeShadow(sc);
            #else
                return 1.0;
            #endif
            }

            half4 FragMarch(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float rawDepth = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).r;

                float3 ro = _WorldSpaceCameraPos;
                float3 far = VL_WorldPos(uv, rawDepth);
                float3 rd = far - ro;
                float dist = length(rd);
                rd = dist > 1e-4 ? rd / dist : float3(0, 0, 1);

                // Sky (raw depth at far plane) still gets a bounded march so shafts read against the sky too.
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 1e-6;
                #else
                    bool isSky = rawDepth >= 0.999999;
                #endif
                float maxD = _VL_Params.y;
                dist = isSky ? maxD : min(dist, maxD);

                int steps = (int)_VL_Params.x;
                float stepLen = dist / max(1, steps);

                // Interleaved-gradient dither breaks up banding; the blur pass smooths the rest.
                float dither = frac(52.9829189 * frac(dot(uv * _VL_Texel.zw, float2(0.06711056, 0.00583715))));
                float t = stepLen * dither;

                float acc = 0.0;
                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float3 p = ro + rd * t;
                    float sh = VL_Shadow(p);
                    float hf = exp(-max(0.0, p.y - _VL_Params2.z) * _VL_Params.w);   // fog thins with height
                    acc += sh * hf;
                    t += stepLen;
                }
                acc /= max(1, steps);

                float cosT = dot(rd, _VL_SunDir.xyz);
                float phase = _VL_Params2.x + VL_HG(cosT, _VL_Params2.y);
                float scatter = acc * phase * _VL_Params.z;
                return half4(_VL_Color.rgb * scatter, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Volumetric Blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur

            half4 FragBlur(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 o = _VL_Texel.xy;
                half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * 0.4;
                c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( o.x, 0)).rgb * 0.15;
                c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-o.x, 0)).rgb * 0.15;
                c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0,  o.y)).rgb * 0.15;
                c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, -o.y)).rgb * 0.15;
                return half4(c, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Volumetric Composite"
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            half4 FragComposite(Varyings input) : SV_Target
            {
                half3 s = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
                return half4(s * _VL_Params2.w * _VL_SunDir.w, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
