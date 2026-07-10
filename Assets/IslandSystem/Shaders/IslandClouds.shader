Shader "IslandSystem/Clouds"
{
    // Stylized billboard clouds — our own, COZY-independent. Meant for a ParticleSystem of view-aligned billboards
    // (the IslandClouds field and the StormWall both use it). Each puff samples a soft fluffy texture for its shape
    // and is shaded with a cheap fake-volume model: a vertical gradient (bright crown → shaded base) plus a push
    // toward the sun side, tinted by the day/night sun colour (global). Soft, cheap, and reads as puffy volume
    // without any raymarching — you can fly through them.
    Properties
    {
        [NoScaleOffset] _MainTex ("Cloud Puff (A = shape)", 2D) = "white" {}
        _LitColor    ("Crown (lit) Color", Color) = (1.0, 0.99, 0.96, 1)
        _ShadowColor ("Base (shadow) Color", Color) = (0.55, 0.60, 0.72, 1)
        _GradientPower ("Crown/base contrast", Range(0.2, 4)) = 1.3
        _SunWrap ("Sun wrap (side lighting)", Range(0, 1)) = 0.5
        _Opacity ("Overall opacity", Range(0, 1)) = 1.0
        _SoftEdge ("Edge softness", Range(0.001, 0.6)) = 0.25
        _SoftDepth ("Soft-particle fade (m)", Float) = 40.0
        _CamFadeNear ("Fly-through fade near (m)", Float) = 18.0
        _CamFadeFar  ("Fly-through fade far (m)", Float) = 110.0
        [Toggle] _UseMesh ("Mesh-sphere puffs (0 = billboard quads, e.g. the storm wall)", Float) = 1
        _BumpAmp  ("Lumpiness amplitude (m)", Float) = 32.0
        _BumpFreq ("Lumpiness frequency (1/m)", Float) = 0.022
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_particles

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            // Driven per-frame by IslandClouds so day/night + sun direction light every cloud (billboards + storm).
            float4 _CloudSunColor;   // rgb = sun light colour, a unused
            float4 _CloudSunDir;     // xyz = direction TO the sun (world), normalized
            float4 _CloudAmbient;    // rgb = sky ambient added to the shaded base

            CBUFFER_START(UnityPerMaterial)
                float4 _LitColor, _ShadowColor;
                float  _GradientPower, _SunWrap, _Opacity, _SoftEdge, _SoftDepth;
                float  _CamFadeNear, _CamFadeFar, _UseMesh;
                float  _BumpAmp, _BumpFreq;
            CBUFFER_END

            float4 _CloudWindOff;   // global: cumulative wind drift since Refill — keeps the lumps glued to the puffs

            // Cheap 3D value noise (for the vertex lumps).
            float hash31 (float3 p) { p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
            float vnoise3 (float3 p)
            {
                float3 ip = floor(p), fp = frac(p);
                fp = fp * fp * (3.0 - 2.0 * fp);
                float v000 = hash31(ip), v100 = hash31(ip + float3(1,0,0));
                float v010 = hash31(ip + float3(0,1,0)), v110 = hash31(ip + float3(1,1,0));
                float v001 = hash31(ip + float3(0,0,1)), v101 = hash31(ip + float3(1,0,1));
                float v011 = hash31(ip + float3(0,1,1)), v111 = hash31(ip + float3(1,1,1));
                float a = lerp(lerp(v000, v100, fp.x), lerp(v010, v110, fp.x), fp.y);
                float b = lerp(lerp(v001, v101, fp.x), lerp(v011, v111, fp.x), fp.y);
                return lerp(a, b, fp.z);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : TEXCOORD1;   // per-particle colour (storm = dark)
                float3 posWS : TEXCOORD2;
                float4 screen: TEXCOORD3;
                half   fog   : TEXCOORD4;
                float2 uv    : TEXCOORD5;
            };

            Varyings vert (Attributes IN)
            {
                Varyings o = (Varyings)0;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);

                // LUMPS: knead each sphere with 2-octave 3D noise along its normal so the silhouette reads as a
                // billowy blob, not a clean ball. Sampled at drift-corrected world pos (the pattern rides along
                // with the wind instead of crawling over the puffs). Mesh path only — billboards keep their quad.
                if (_UseMesh > 0.5)
                {
                    float3 sp = (posWS - _CloudWindOff.xyz) * _BumpFreq;
                    float n = vnoise3(sp) * 0.7 + vnoise3(sp * 2.3 + 17.1) * 0.3;
                    posWS += nWS * (n * 2.0 - 1.0) * _BumpAmp;
                }

                o.posWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = nWS;
                o.color = IN.color;
                o.uv = IN.uv;
                o.screen = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float shape, vg;
                if (_UseMesh > 0.5)
                {
                    // MESH-SPHERE puffs (the cloud field): world-anchored spheres that never turn with the camera.
                    // Opaque body; only the silhouette (grazing normals) feathers out, so overlapping spheres merge
                    // into one solid lumpy mass. Fade starts at ndv 0.35 so alpha dies BEFORE the low-poly sphere's
                    // geometric silhouette (otherwise the polygon edges show as a sawtooth rim).
                    float3 N = normalize(i.normalWS);
                    float3 V = SafeNormalize(_WorldSpaceCameraPos - i.posWS);
                    float ndv = saturate(dot(N, V));
                    shape = smoothstep(0.35, 0.35 + max(0.15, _SoftEdge), ndv);
                    vg = lerp(1.0 - 0.10 * _GradientPower, 1.0, saturate(N.y * 0.5 + 0.75));
                }
                else
                {
                    // BILLBOARD quads (the far storm wall — thousands of puffs, spheres would cost millions of tris
                    // and their slow camera-turn is imperceptible at the world edge). Dense-core soft-rim texture.
                    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                    shape = smoothstep(0.12, 0.12 + max(0.1, _SoftEdge * 1.8), tex.a);
                    vg = lerp(1.0 - 0.10 * _GradientPower, 1.0, saturate(i.uv.y * 1.4));
                }
                shape *= i.color.a * _Opacity;

                // Shading comes BAKED PER-PARTICLE (i.color = lit/shadow gradient by the puff's height within its
                // whole cloud, computed on the CPU) so the cloud shades as ONE mass, not per-ball.
                float3 col = i.color.rgb * _LitColor.rgb * vg;
                col *= _CloudSunColor.rgb;                             // day/night sun colour
                col += _CloudAmbient.rgb * 0.12;                       // faint sky bounce

                // Soft-particle: fade where the billboard meets solid geometry so puffs don't hard-clip islands.
                float2 suv = i.screen.xy / max(1e-5, i.screen.w);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                shape *= saturate((sceneEye - i.screen.w) / max(0.1, _SoftDepth));

                // Fly-through fade: puffs you fly INTO dissolve, so you pass through with a clear bubble instead of
                // a wall of white (COZY-style). Distance from the camera to this puff, faded over [near, far].
                float camDist = distance(i.posWS, _WorldSpaceCameraPos);
                shape *= smoothstep(_CamFadeNear, _CamFadeFar, camDist);

                col = MixFog(col, i.fog);
                return half4(col, saturate(shape));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
