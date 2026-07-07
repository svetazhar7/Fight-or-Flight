Shader "IslandSystem/Foliage"
{
    // URP lit shader for prefab foliage (flowers, ferns, bushes) that adds a gentle root-anchored WIND SWAY
    // (same feel as the grass shader) on top of normal PBR lighting + shadows + normal maps. The bend is
    // scaled by OBJECT-space height / _WindHeight, so the base stays planted and only the upper parts sway;
    // it also reacts to the shared player interactors (_GrassInteractors) so bushes part underfoot like grass.
    // Alpha-cutout (Opaque queue) with matching ShadowCaster / DepthOnly / DepthNormals passes so the foliage
    // casts shadows and writes depth for COZY fog / SSAO exactly where the swaying geometry actually is.
    Properties
    {
        [MainTexture] _BaseMap ("Base Map (RGBA)", 2D) = "white" {}
        [MainColor]  _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.1

        [Toggle] _UseFoliageFade ("Distance Fade (streamed flowers/bushes; OFF for tree leaves)", Float) = 1

        [Header(Wind)]
        _WindHeight   ("Wind Height (plant height, m)", Float) = 2.0
        _WindStrength ("Wind Strength", Float) = 0.12
        _BendStrength ("Interactor Bend", Float) = 1.0

        [Header(Wind LOD)]
        _WindFadeStart ("Wind LOD Start (m — wind fades out)", Float) = 60.0
        _WindFadeEnd   ("Wind LOD End (m — no animation past here)", Float) = 150.0
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #define MAX_GRASS_INTERACTORS 16
        float4 _GrassInteractors[MAX_GRASS_INTERACTORS]; // xyz world pos, w radius (shared with the grass shader)
        int    _GrassInteractorCount;

        // ONE island-wide wind field (set globally by IslandWind) — the SAME uniforms the grass shader reads,
        // so flowers/bushes sway in exact phase with the grass around them.
        float _IslandWindSpeed;
        float _IslandWindScale;

        // Distance fade (set globally by IslandFlowerField, matching the grass fade): the plant SHRINKS into
        // its root as it nears the streaming radius, so flowers/bushes appear and vanish SMOOTHLY like grass
        // instead of popping when a chunk (de)spawns. Uses the object pivot's XZ distance to the render camera
        // — the same camera the flowers stream around. Disabled while unset (End <= Start), so any foliage that
        // isn't streamed renders at full size.
        float _FoliageFadeStart;
        float _FoliageFadeEnd;

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float  _Cutoff, _BumpScale, _Smoothness;
            float  _WindHeight, _WindStrength, _BendStrength;
            float  _WindFadeStart, _WindFadeEnd;   // distance LOD: wind animation fades out between these (perf)
            float  _UseFoliageFade;   // 1 = streamed foliage fades with distance (flowers/bushes); 0 = tree leaves (never fade)
        CBUFFER_END

        // Distance fade: streamed flowers/bushes SHRINK into their root near the streaming radius so they appear
        // /vanish smoothly (globals _FoliageFadeStart/End set by IslandFlowerField). Gated per material by
        // _UseFoliageFade so TREE LEAVES that share this shader stay full size at any distance (IslandTreeRenderer
        // sets it to 0). rootWS MUST be the instance pivot via TransformObjectToWorld(0) — under GPU instancing,
        // reading unity_ObjectToWorld._m03 from a helper returns identity and collapses every instance to origin.
        float3 FoliageFadeOS(float3 positionOS, float3 rootWS)
        {
            if (_UseFoliageFade < 0.5 || _FoliageFadeEnd <= _FoliageFadeStart + 0.5) return positionOS;
            float dCam = distance(rootWS.xz, _WorldSpaceCameraPos.xz);
            float fade = saturate((dCam - _FoliageFadeStart) / (_FoliageFadeEnd - _FoliageFadeStart));
            return positionOS * (1.0 - fade);   // collapse toward the (ground-level) pivot
        }

        // World-space wind + player-flatten, anchored by object-space height (root pinned, tips sway).
        // IDENTICAL formula and phase to the grass shader's GrassVertexWS — same world position at the same
        // time gives the same offset, so a flower leans exactly with the grass blades beside it.
        float3 ApplyFoliageWind(float3 positionWS, float3 positionOS)
        {
            // WIND LOD: fade the whole animation out with camera distance and SKIP it entirely past _WindFadeEnd —
            // distant foliage stops paying for the sway + interactor loop (and its verts go static, so the GPU can
            // reuse them). The fade is smooth so leaves don't visibly "freeze" at the boundary. All passes call this
            // same function, so forward / shadow / depth stay in lockstep (no z-fighting between animated & static).
            float dCam = distance(positionWS.xz, _WorldSpaceCameraPos.xz);
            float windScale = 1.0 - saturate((dCam - _WindFadeStart) / max(0.01, _WindFadeEnd - _WindFadeStart));
            if (windScale <= 0.001) return positionWS;   // far away: no animation at all

            float bend = saturate(positionOS.y / max(0.01, _WindHeight)) * windScale;

            float2 wp = positionWS.xz * _IslandWindScale; float ph = _Time.y * _IslandWindSpeed;
            float w = sin(ph + wp.x + wp.y) + 0.5 * sin(ph * 1.7 + wp.x * 0.7 - wp.y * 1.3);
            positionWS.x += w * _WindStrength * bend;
            positionWS.z += 0.6 * w * _WindStrength * bend;

            [loop] for (int k = 0; k < _GrassInteractorCount; k++)
            {
                float ir = _GrassInteractors[k].w; if (ir <= 0.0) continue;
                float2 d = positionWS.xz - _GrassInteractors[k].xz; float dl = length(d);
                if (dl < ir)
                {
                    float vfall = saturate(1.0 - abs(positionWS.y - _GrassInteractors[k].y) / max(1.0, ir * 1.5));
                    float f = (1 - dl / ir) * _BendStrength * bend * vfall;
                    positionWS.xz += normalize(d + 1e-4) * f * ir * 0.35;
                    positionWS.y -= f * 0.5;
                }
            }
            return positionWS;
        }
        ENDHLSL

        // ---- Forward lit ----
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 posWS : TEXCOORD1;
                float3 nWS : TEXCOORD2;
                float4 tWS : TEXCOORD3;   // xyz tangent, w sign
                half4  fogFactorAndVertexLight : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
            };

            Varyings vert (Attributes IN)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);   // makes unity_ObjectToWorld per-instance (RenderMeshInstanced)
                float3 rootWS = TransformObjectToWorld(float3(0,0,0));   // per-instance pivot (see FoliageFadeOS)
                float3 posOS = FoliageFadeOS(IN.positionOS.xyz, rootWS);
                float3 posWS = TransformObjectToWorld(posOS);
                posWS = ApplyFoliageWind(posWS, posOS);

                VertexNormalInputs ni = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                o.posWS = posWS;
                o.nWS = ni.normalWS;
                o.tWS = float4(ni.tangentWS, IN.tangentOS.w * GetOddNegativeScale());

                half3 vertexLight = VertexLighting(posWS, ni.normalWS);
                half fog = ComputeFogFactor(o.positionCS.z);
                o.fogFactorAndVertexLight = half4(fog, vertexLight);
                o.shadowCoord = TransformWorldToShadowCoord(posWS);
                return o;
            }

            half4 frag (Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(tex.a - _Cutoff);

                float3 nWS = normalize(i.nWS);
                if (!IS_FRONT_VFACE(facing, true, false)) nWS = -nWS;

                // Tangent-space normal map -> world.
                float3 tWS = normalize(i.tWS.xyz);
                float3 bWS = normalize(cross(nWS, tWS) * i.tWS.w);
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                nWS = normalize(nTS.x * tWS + nTS.y * bWS + nTS.z * nWS);

                // STYLIZED lighting matched to the grass (IslandGrass): a soft half-lambert keeps shaded petals
                // lit instead of near-black (hard PBR made flowers read dark next to the wrap-lit grass), plus SH
                // ambient; shadows only darken to 0.35 like the terrain. Flat toon look, consistent with the grass.
                Light L = GetMainLight(i.shadowCoord);
                float ndl = saturate(dot(nWS, L.direction)) * 0.5 + 0.5;   // half-lambert 0.5..1
                float3 sun = L.color.rgb * lerp(0.35, 1.0, ndl);
                float3 ambient = SampleSH(nWS) + 0.28;                    // small lift so shaded petals aren't muddy
                float3 lighting = min(sun + ambient, 1.25);
                lighting *= lerp(0.35, 1.0, L.shadowAttenuation);          // shadowed side dims but never to black

                float3 col = tex.rgb * lighting;
                col = MixFog(col, i.fogFactorAndVertexLight.x);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // ---- Shadow caster (wind-matched so shadows sway with the geometry) ----
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V shadowVert (A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 rootWS = TransformObjectToWorld(float3(0,0,0));
                float3 posOS = FoliageFadeOS(IN.positionOS.xyz, rootWS);
                float3 posWS = TransformObjectToWorld(posOS);
                posWS = ApplyFoliageWind(posWS, posOS);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return o;
            }

            half4 shadowFrag (V i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ---- Depth prepass ----
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V depthVert (A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 rootWS = TransformObjectToWorld(float3(0,0,0));
                float3 posOS = FoliageFadeOS(IN.positionOS.xyz, rootWS);
                float3 posWS = TransformObjectToWorld(posOS);
                posWS = ApplyFoliageWind(posWS, posOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return o;
            }

            half depthFrag (V i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ---- Depth normals prepass ----
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex dnVert
            #pragma fragment dnFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 nWS : TEXCOORD1; };

            V dnVert (A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 rootWS = TransformObjectToWorld(float3(0,0,0));
                float3 posOS = FoliageFadeOS(IN.positionOS.xyz, rootWS);
                float3 posWS = TransformObjectToWorld(posOS);
                posWS = ApplyFoliageWind(posWS, posOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                o.nWS = TransformObjectToWorldNormal(IN.normalOS);
                return o;
            }

            half4 dnFrag (V i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                return half4(normalize(i.nWS) * 0.5 + 0.5, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
