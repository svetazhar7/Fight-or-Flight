Shader "IslandSystem/Water"
{
    // Our own stylized water for URP 17 (Render Graph safe) — a from-scratch rewrite inspired by Poseidon 2 but
    // fully custom. It composites the sea itself from the camera Opaque + Depth textures (which the stock
    // Poseidon 2 ShaderGraph fails to read on Unity 6), so it can be genuinely transparent AND draw light on the
    // bottom:
    //   • Beer-Lambert ABSORPTION — the seabed shows through, tinted + darkened with depth (clear shallows →
    //     opaque deep), so it reads as real water, not just the raw sand;
    //   • TEXTURE CAUSTICS (sun glints) projected onto the seabed in world space — two scrolling samples min'd
    //     together so they shimmer without visible tiling, seen through the water;
    //   • a little REFRACTION of the seabed by the wave normal;
    //   • SMOOTH analytic wave normals (rolling swell, not faceted) with a reflect-vector SUN glint;
    //   • waves that CALM DOWN toward the shore (depth-attenuated) so shallows are glassy, deep water rolls;
    //   • foam ONLY at the shoreline, broken into bubbles by noise.
    // Renders opaque (it draws the seabed itself) at the Transparent queue so the opaque textures are ready.
    Properties
    {
        [Header(Water colour and transparency)]
        _DeepColor    ("Deep Water Color", Color) = (0.02, 0.20, 0.40, 1)
        _ShallowColor ("Shallow Water Tint", Color) = (0.30, 0.72, 0.72, 1)
        _AbsorbColor  ("Absorption per channel (red highest = cyan water)", Color) = (0.90, 0.30, 0.22, 1)
        _WaterDensity ("Water Density (higher = murkier / less see-through)", Range(0.02, 1.5)) = 0.32

        [Header(Caustics (sun on the seabed))]
        [NoScaleOffset] _CausticsTex ("Caustics Texture (grayscale, tileable)", 2D) = "black" {}
        _CausticsColor    ("Caustics Color", Color) = (1, 0.98, 0.85, 1)
        _CausticsStrength ("Caustics Strength", Range(0,3)) = 0.9
        _CausticsScale    ("Caustics Scale (1/m)", Float) = 0.12
        _CausticsSpeed    ("Caustics Speed", Float) = 0.6
        _CausticsSharp    ("Caustics Sharpness", Range(1,6)) = 2.2
        _CausticsDepth    ("Caustics fade depth (m)", Float) = 6.0

        [Header(Foam (shoreline only))]
        _FoamColor  ("Foam Color", Color) = (1,1,1,1)
        _FoamDepth  ("Foam width at shore (m)", Float) = 1.6
        _FoamNoiseScale ("Foam bubble scale (1/m)", Float) = 0.6
        _FoamSpeed  ("Foam scroll speed", Float) = 0.12
        _FoamSharpness ("Foam bubble sharpness", Range(0.02,0.5)) = 0.16

        [Header(Waves (smooth, calm at shore))]
        _WaveHeight ("Wave Height (m)", Float) = 0.35
        _WaveScale  ("Wave Scale (1/m)", Float) = 0.08
        _WaveSpeed  ("Wave Speed", Float) = 0.9
        _WaveShoreFade ("Wave calm-down depth at shore (m)", Float) = 3.5
        _NormalStrength ("Surface ripple strength", Range(0,3)) = 1.3

        [Header(Sun and sky)]
        _SunStrength  ("Sun Reflection Strength", Range(0,20)) = 3.0
        _SunSharpness ("Sun Reflection Sharpness", Range(1,2048)) = 600
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_CausticsTex);   SAMPLER(sampler_CausticsTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _AbsorbColor, _CausticsColor, _FoamColor, _FresnelTint;
                float  _WaterDensity;
                float  _CausticsStrength, _CausticsScale, _CausticsSpeed, _CausticsSharp, _CausticsDepth;
                float  _FoamDepth, _FoamNoiseScale, _FoamSpeed, _FoamSharpness;
                float  _WaveHeight, _WaveScale, _WaveSpeed, _WaveShoreFade, _NormalStrength;
                float  _SunStrength, _SunSharpness, _FresnelPower;
            CBUFFER_END

            float hash21 (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise (float2 p)
            {
                float2 ip = floor(p), fp = frac(p);
                fp = fp * fp * (3.0 - 2.0 * fp);
                float a = hash21(ip), b = hash21(ip + float2(1, 0)), c = hash21(ip + float2(0, 1)), d = hash21(ip + float2(1, 1));
                return lerp(lerp(a, b, fp.x), lerp(c, d, fp.x), fp.y);
            }
            float fbm (float2 p) { return 0.6 * vnoise(p) + 0.3 * vnoise(p * 2.1 + 7.3) + 0.1 * vnoise(p * 4.7 + 19.1); }

            // The three swell components (dir.xy, freq mult .z, amp mult .w) — shared by the height + analytic normal.
            static const float4 kWaves[3] = {
                float4( 1.0,  0.35, 1.0, 0.55),
                float4(-0.6,  0.8,  1.9, 0.30),
                float4( 0.2, -1.0,  3.3, 0.18)
            };

            float WaveHeight (float2 xz)
            {
                float h = 0.0, t = _Time.y * _WaveSpeed;
                [unroll] for (int i = 0; i < 3; i++)
                {
                    float2 dir = normalize(kWaves[i].xy);
                    float freq = _WaveScale * kWaves[i].z;
                    float amp  = _WaveHeight * kWaves[i].w;
                    h += amp * sin(dot(dir, xz) * freq + t * (1.0 + 0.25 * i));
                }
                return h;
            }

            // Analytic SMOOTH normal from the same swell (d/dxz of the sines) — rolling waves, no facets. Ripple
            // detail + the swell both fade to flat as `atten` → 0 near the shore.
            float3 WaveNormal (float2 xz, float atten)
            {
                float t = _Time.y * _WaveSpeed;
                float2 g = 0.0;
                [unroll] for (int i = 0; i < 3; i++)
                {
                    float2 dir = normalize(kWaves[i].xy);
                    float freq = _WaveScale * kWaves[i].z;
                    float amp  = _WaveHeight * kWaves[i].w;
                    g += amp * freq * cos(dot(dir, xz) * freq + t * (1.0 + 0.25 * i)) * dir;
                }
                // NOTE: no fine high-frequency ripple — from above it reads as grainy speckle. The smooth swell
                // above is the whole normal; keep it clean.
                g *= atten * _NormalStrength;
                return normalize(float3(-g.x, 1.0, -g.y));
            }

            // Texture caustics: two scrolling samples min'd together → animated light web that never reads as a tile.
            float CausticsTex (float2 p)
            {
                float t = _Time.y * _CausticsSpeed;
                float c1 = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, p + float2(0.6, 0.35) * t * 0.12).r;
                float c2 = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, p * 1.37 + float2(-0.5, 0.7) * t * 0.1 + 4.1).r;
                return pow(min(c1, c2), _CausticsSharp);
            }

            float SampleDepthLOD0 (float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0).r;
            }

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posWS  : TEXCOORD0;
                float4 screen : TEXCOORD1;   // ComputeScreenPos; .w = eye depth of the water surface
                half   fog    : TEXCOORD2;
                half   atten  : TEXCOORD3;   // wave strength (0 at shore .. 1 deep)
            };

            Varyings vert (Attributes IN)
            {
                Varyings o = (Varyings)0;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);

                // Read the seabed depth UNDER this vertex (pre-wave) so the swell can calm down toward the shore:
                // shallow water (thin column) → tiny waves; deep water → full swell.
                float4 clip0 = TransformWorldToHClip(posWS);
                float4 sp0   = ComputeScreenPos(clip0);
                float2 uv0   = sp0.xy / max(1e-5, sp0.w);
                float  seabedEye = LinearEyeDepth(SampleDepthLOD0(uv0), _ZBufferParams);
                float  thick0    = max(0.0, seabedEye - sp0.w);
                float  atten     = smoothstep(0.0, max(0.05, _WaveShoreFade), thick0);

                posWS.y += WaveHeight(posWS.xz) * atten;
                o.posWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.atten = atten;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 N = WaveNormal(i.posWS.xz, i.atten);

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

                // Texture caustics on the seabed: fade with depth (shallow-only) and only if the sun is up.
                float causticFade = saturate(1.0 - thickness / max(0.1, _CausticsDepth)) * saturate(main.direction.y * 2.0);
                float caust = CausticsTex(seabedWS.xz * _CausticsScale) * _CausticsStrength * causticFade;
                seabed += caust * _CausticsColor.rgb * main.color.rgb;

                // Beer-Lambert absorption: how much of the (caustic-lit) seabed survives the water column. The water's
                // own colour blends from a bright SHALLOW tint into the DEEP colour with depth.
                float3 transmit = exp(-thickness * _AbsorbColor.rgb * _WaterDensity);
                float  depthT   = saturate(1.0 - exp(-thickness * _WaterDensity * 0.9));
                float3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
                float3 col = seabed * transmit + waterCol * (1.0 - transmit);

                // ---- shoreline foam (bubbly), only where the water is shallow ----
                float edge = 1.0 - smoothstep(0.0, max(0.01, _FoamDepth), thickness);
                float2 fuv = i.posWS.xz * _FoamNoiseScale + _Time.y * _FoamSpeed * float2(0.3, 0.55);
                float bubbles = fbm(fuv) * 0.7 + fbm(fuv * 3.3 + 4.0) * 0.3;
                float foam = smoothstep(1.0 - edge - _FoamSharpness, 1.0 - edge + _FoamSharpness, bubbles) * step(0.03, edge);
                col = lerp(col, _FoamColor.rgb, foam);

                // ---- surface: sun glint (reflect vector) + fresnel sky ----
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
