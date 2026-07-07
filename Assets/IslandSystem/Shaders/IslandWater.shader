Shader "IslandSystem/Water"
{
    // Our own stylized water for URP 17 (Render Graph safe) — a from-scratch rewrite inspired by Poseidon 2 but
    // fully custom. It composites the sea itself from the camera Opaque + Depth textures (which the stock
    // Poseidon 2 ShaderGraph fails to read on Unity 6), so it can be genuinely transparent AND draw light on the
    // bottom:
    //   • Beer-Lambert ABSORPTION — the seabed shows through, tinted + darkened with depth (clear shallows →
    //     opaque deep), so it reads as real water, not just the raw sand;
    //   • animated CAUSTICS (sun glints) projected onto the seabed in world space, seen through the water;
    //   • a little REFRACTION of the seabed by the wave normal;
    //   • FLAT-shaded wave-displaced tris → low-poly faceted surface + a reflect-vector SUN glint;
    //   • foam ONLY at the shoreline, broken into bubbles by noise.
    // Renders opaque (it draws the seabed itself) at the Transparent queue so the opaque textures are ready.
    Properties
    {
        [Header(Water colour and transparency)]
        _DeepColor    ("Deep Water Color", Color) = (0.02, 0.20, 0.40, 1)
        _AbsorbColor  ("Absorption per channel (red highest = cyan water)", Color) = (0.90, 0.30, 0.22, 1)
        _WaterDensity ("Water Density (higher = murkier / less see-through)", Range(0.02, 1.5)) = 0.32

        [Header(Caustics (sun on the seabed))]
        _CausticsColor    ("Caustics Color", Color) = (1, 0.98, 0.85, 1)
        _CausticsStrength ("Caustics Strength", Range(0,3)) = 0.6
        _CausticsScale    ("Caustics Scale (1/m)", Float) = 0.35
        _CausticsSpeed    ("Caustics Speed", Float) = 0.5
        _CausticsSharp    ("Caustics Sharpness", Range(1,12)) = 6.0
        _CausticsDepth    ("Caustics fade depth (m)", Float) = 5.0

        [Header(Foam (shoreline only))]
        _FoamColor  ("Foam Color", Color) = (1,1,1,1)
        _FoamDepth  ("Foam width at shore (m)", Float) = 1.4
        _FoamNoiseScale ("Foam bubble scale (1/m)", Float) = 0.6
        _FoamSpeed  ("Foam scroll speed", Float) = 0.12
        _FoamSharpness ("Foam bubble sharpness", Range(0.02,0.5)) = 0.16

        [Header(Waves (low poly))]
        _WaveHeight ("Wave Height (m)", Float) = 0.4
        _WaveScale  ("Wave Scale (1/m)", Float) = 0.09
        _WaveSpeed  ("Wave Speed", Float) = 1.0

        [Header(Sun and sky)]
        _SunStrength  ("Sun Reflection Strength", Range(0,20)) = 3.0
        _SunSharpness ("Sun Reflection Sharpness", Range(1,2048)) = 700
        _FresnelPower ("Fresnel Power", Range(1,8)) = 4.0
        _FresnelTint  ("Fresnel / Sky Tint", Color) = (0.55, 0.72, 0.85, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend One Zero
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _AbsorbColor, _CausticsColor, _FoamColor, _FresnelTint;
                float  _WaterDensity;
                float  _CausticsStrength, _CausticsScale, _CausticsSpeed, _CausticsSharp, _CausticsDepth;
                float  _FoamDepth, _FoamNoiseScale, _FoamSpeed, _FoamSharpness;
                float  _WaveHeight, _WaveScale, _WaveSpeed;
                float  _SunStrength, _SunSharpness, _FresnelPower;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posWS  : TEXCOORD0;
                float4 screen : TEXCOORD1;   // ComputeScreenPos; .w = eye depth of the water surface
                half   fog    : TEXCOORD2;
            };

            float hash21 (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise (float2 p)
            {
                float2 ip = floor(p), fp = frac(p);
                fp = fp * fp * (3.0 - 2.0 * fp);
                float a = hash21(ip), b = hash21(ip + float2(1, 0)), c = hash21(ip + float2(0, 1)), d = hash21(ip + float2(1, 1));
                return lerp(lerp(a, b, fp.x), lerp(c, d, fp.x), fp.y);
            }
            float fbm (float2 p) { return 0.6 * vnoise(p) + 0.3 * vnoise(p * 2.1 + 7.3) + 0.1 * vnoise(p * 4.7 + 19.1); }

            // Animated caustics: bright thin veins where two scrolling noise fields cross — sunlight focused on a
            // seabed. Sharpened so it reads as crisp glints (mostly 0 between the veins).
            float Caustics (float2 p)
            {
                float t = _Time.y * _CausticsSpeed;
                float a = vnoise(p + float2(t * 0.6,  t * 0.35));
                float b = vnoise(p * 1.4 + float2(-t * 0.5, t * 0.7) + 3.3);
                float c = pow(saturate(1.0 - abs(a - b)), _CausticsSharp);
                return c * c;
            }

            float WaveHeight (float2 xz)
            {
                float h = 0.0;
                float t = _Time.y * _WaveSpeed;
                const float4 waves[3] = {
                    float4( 1.0,  0.35, 1.0, 0.55),
                    float4(-0.6,  0.8,  1.9, 0.30),
                    float4( 0.2, -1.0,  3.3, 0.18)
                };
                [unroll] for (int i = 0; i < 3; i++)
                {
                    float2 dir = normalize(waves[i].xy);
                    float freq = _WaveScale * waves[i].z;
                    float amp  = _WaveHeight * waves[i].w;
                    h += amp * sin(dot(dir, xz) * freq + t * (1.0 + 0.25 * i));
                }
                return h;
            }

            Varyings vert (Attributes IN)
            {
                Varyings o = (Varyings)0;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                posWS.y += WaveHeight(posWS.xz);
                o.posWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // FLAT per-triangle normal from the wave-displaced surface → visible low-poly facets.
                float3 N = normalize(cross(ddy(i.posWS), ddx(i.posWS)));
                if (N.y < 0) N = -N;

                float2 uv = i.screen.xy / i.screen.w;
                float surfEye = i.screen.w;

                // Refract the seabed a little with the surface normal; fall back if it lands on a foreground object.
                float2 refrUV = uv + N.xz * 0.03;
                float rawR = SampleSceneDepth(refrUV);
                float sceneEyeR = LinearEyeDepth(rawR, _ZBufferParams);
                if (sceneEyeR < surfEye) { refrUV = uv; rawR = SampleSceneDepth(uv); sceneEyeR = LinearEyeDepth(rawR, _ZBufferParams); }
                float thickness = max(0.0, sceneEyeR - surfEye);

                float3 seabed   = SampleSceneColor(refrUV).rgb;
                float3 seabedWS = ComputeWorldSpacePosition(refrUV, rawR, UNITY_MATRIX_I_VP);
                Light main = GetMainLight();

                // Caustics on the seabed: crisp veins, fade with depth (shallow-only) and only if the sun is up.
                float causticFade = saturate(1.0 - thickness / max(0.1, _CausticsDepth)) * saturate(main.direction.y * 2.0);
                float caust = Caustics(seabedWS.xz * _CausticsScale) * _CausticsStrength * causticFade;
                seabed += caust * _CausticsColor.rgb;

                // Beer-Lambert absorption: how much of the (caustic-lit) seabed survives the water column, tinted so
                // the water goes cyan→blue with depth. What's absorbed becomes the water's own colour.
                float3 transmit = exp(-thickness * _AbsorbColor.rgb * _WaterDensity);
                float3 col = seabed * transmit + _DeepColor.rgb * (1.0 - transmit);

                // ---- shoreline foam (bubbly), only where the water is shallow ----
                float edge = 1.0 - smoothstep(0.0, max(0.01, _FoamDepth), thickness);
                float2 fuv = i.posWS.xz * _FoamNoiseScale + _Time.y * _FoamSpeed * float2(0.3, 0.55);
                float bubbles = fbm(fuv) * 0.7 + fbm(fuv * 3.3 + 4.0) * 0.3;
                float foam = smoothstep(1.0 - edge - _FoamSharpness, 1.0 - edge + _FoamSharpness, bubbles) * step(0.03, edge);
                col = lerp(col, _FoamColor.rgb, foam);

                // ---- surface: sun glint (reflect vector, glitters across facets) + fresnel sky ----
                float3 V = SafeNormalize(GetWorldSpaceViewDir(i.posWS));
                float sun = pow(saturate(dot(reflect(-V, N), main.direction)), _SunSharpness) * _SunStrength;
                col += main.color.rgb * sun;

                float fres = pow(1.0 - saturate(dot(N, V)), _FresnelPower);
                col = lerp(col, _FresnelTint.rgb, fres * 0.35);

                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
