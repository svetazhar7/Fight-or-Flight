Shader "IslandSystem/Grass"
{
    // GPU-instanced foliage (URP), Cyanilux "GPU Instanced Grass" style — the full INDIRECT setup: drawn with
    // Graphics.RenderMeshIndirect; a compute shader (GrassFrustumCull.compute) appends the ids of instances
    // inside the camera frustum to _VisibleIDs, so SV_InstanceID indexes _VisibleIDs first, then the visible id
    // fetches the per-instance WORLD matrix from _PerInstanceData (no unity_ObjectToWorld, no 1023-array limit).
    // Wind sway + player flatten per-vertex (scaled by object-space height so the root is pinned and the tips
    // sway). Alpha-cutout texture, root→tip colour gradient + world-noise dry patches, per-instance dithered
    // distance fade. Has DepthOnly + DepthNormals passes so depth-texture effects (COZY fog, SSAO, water) see
    // the grass — without them the blades were "ghosted" with the background's fog/depth.
    Properties
    {
        [MainTexture] _MainTex ("Texture (RGBA)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.3
        _BottomColor ("Bottom Color (root tint)", Color) = (1,1,1,1)
        _TopColor    ("Top Color (tip tint)",     Color) = (1,1,1,1)
        _Tiles       ("Texture Tiles (NxN atlas, 1 = off)", Float) = 1
        _DryBottomColor ("Dry Bottom Color", Color) = (1,1,1,1)
        _DryTopColor    ("Dry Top Color",    Color) = (1,1,1,1)
        _ColorVariation ("Color Variation (0 = off)", Range(0,1)) = 0
        _VariationScale ("Variation Patch Scale (1/m)", Float) = 0.03
        _FadeStart ("Distance Fade Start (m)", Float) = 100000
        _FadeEnd   ("Distance Fade End (m)",   Float) = 100001
        _WindHeight   ("Wind Height (blade height)", Float) = 0.5
        _WindStrength ("Wind Strength", Float) = 0.15
        _WindSpeed    ("Wind Speed",    Float) = 1.0
        _WindScale    ("Wind Scale",    Float) = 0.15
        _BendStrength ("Interactor Bend", Float) = 1.0
        _AmbientBoost ("Ambient Boost", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #define MAX_GRASS_INTERACTORS 16
        float4 _GrassInteractors[MAX_GRASS_INTERACTORS]; // xyz world pos, w radius
        int    _GrassInteractorCount;

        StructuredBuffer<float4x4> _PerInstanceData;     // per-instance WORLD matrices (ALL instances of the chunk)
        StructuredBuffer<uint>     _VisibleIDs;          // ids that survived GPU frustum culling (GrassFrustumCull.compute)
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _BottomColor, _TopColor;
            float4 _DryBottomColor, _DryTopColor;
            float  _Cutoff, _Tiles, _ColorVariation, _VariationScale;
            float  _FadeStart, _FadeEnd;
            float  _WindHeight, _WindStrength, _WindSpeed, _WindScale, _BendStrength, _AmbientBoost;
        CBUFFER_END

        // Cheap 2D value noise for the large-scale colour variation. WORLD-space input → patches are
        // continuous across chunks, layers and biome borders (every material samples the same field).
        float hash21 (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float vnoise (float2 p)
        {
            float2 ip = floor(p), fp = frac(p);
            fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);   // quintic fade — hides cell borders
            float a = hash21(ip), b = hash21(ip + float2(1, 0)), c = hash21(ip + float2(0, 1)), d = hash21(ip + float2(1, 1));
            return lerp(lerp(a, b, fp.x), lerp(c, d, fp.x), fp.y);
        }

        // Organic patch noise. Raw bilinear value noise makes SQUARE, axis-aligned blobs (the lattice shows
        // through). Domain warping + rotated octaves break the grid so the dry/lush patches look natural.
        float patchNoise (float2 p)
        {
            float2 w = float2(vnoise(p * 0.9 + 13.7), vnoise(p * 0.9 + 71.3));
            p += (w - 0.5) * 2.2;                                   // domain warp: bends the lattice
            const float2x2 R1 = float2x2( 0.5403, -0.8415, 0.8415,  0.5403);  // rot 1 rad
            const float2x2 R2 = float2x2(-0.4161, -0.9093, 0.9093, -0.4161);  // rot 2 rad
            float n  = 0.55 * vnoise(mul(R1, p));
            n       += 0.30 * vnoise(mul(R2, p) * 2.13 + 7.7);
            n       += 0.15 * vnoise(p * 4.37 + 19.1);
            return n;
        }

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; uint instanceID : SV_InstanceID; };

        // Shared vertex math for ALL passes. The depth passes MUST move vertices identically to the forward
        // pass (same fade/wind/flatten), or the depth texture wouldn't match what's actually shaded.
        void GrassVertexWS (Attributes IN, out float3 posWS, out float3 nWS, out float2 uv, out float grad)
        {
            uint visId = _VisibleIDs[IN.instanceID];               // indirect draw: instanceID walks the VISIBLE list
            float4x4 m = _PerInstanceData[visId];                  // this instance's world matrix

            // Distance fade, DITHERED per instance: each blade shrinks into the ground at its own random
            // distance inside the [_FadeStart.._FadeEnd] band, so the field gradually THINS OUT with distance.
            float2 rootXZ = float2(m._m03, m._m23);
            float dCam = distance(rootXZ, _WorldSpaceCameraPos.xz);
            uint fh = visId * 2654435761u + 1013904223u;
            fh = (fh ^ (fh >> 16)) * 2246822519u;
            float thr01 = ((fh >> 8) & 0xFFFFFF) / 16777215.0;
            float thr = lerp(_FadeStart, _FadeEnd, thr01);      // this blade's personal vanish distance
            float fade = saturate((dCam - thr) / 6.0);          // ...over which it shrinks within ~6 m
            float3 posOS = IN.positionOS.xyz * (1.0 - fade);

            posWS = mul(m, float4(posOS, 1.0)).xyz;
            nWS   = normalize(mul(m, float4(IN.normalOS, 0.0)).xyz);

            // Bend from OBJECT-space height (root = 0, tip = 1) — root is pinned, only the tips sway.
            float bend = saturate(posOS.y / max(0.01, _WindHeight));

            float2 wp = posWS.xz * _WindScale; float ph = _Time.y * _WindSpeed;
            float w = sin(ph + wp.x + wp.y) + 0.5 * sin(ph * 1.7 + wp.x * 0.7 - wp.y * 1.3);
            posWS.x += w * _WindStrength * bend;
            posWS.z += 0.6 * w * _WindStrength * bend;

            [loop] for (int k = 0; k < _GrassInteractorCount; k++)
            {
                float ir = _GrassInteractors[k].w; if (ir <= 0.0) continue;
                float2 d = posWS.xz - _GrassInteractors[k].xz; float dl = length(d);
                if (dl < ir) { float f = (1 - dl / ir) * _BendStrength * bend; posWS.xz += normalize(d + 1e-4) * f * ir * 0.35; posWS.y -= f * 0.5; }
            }

            // Cyanilux flipbook: the texture is an NxN atlas of blade-clump variants; each instance picks a
            // random tile. visId is STABLE per instance (indexes the persistent matrix buffer), so the tile
            // doesn't flicker when frustum culling reshuffles SV_InstanceID. Y scaled by 0.9 so the tile
            // above doesn't leak through at the blade tips (the tutorial's Tiling-And-Offset trick).
            uv = TRANSFORM_TEX(IN.uv, _MainTex);
            if (_Tiles > 1.5)
            {
                uint n = (uint)_Tiles;
                uint h = visId * 747796405u + 2891336453u;
                h = ((h >> ((h >> 28) + 4u)) ^ h) * 277803737u;
                h = (h >> 22) ^ h;
                uint tile = h % (n * n);
                uv = (IN.uv * float2(1.0, 0.9) + float2(tile % n, tile / n)) / n;
            }
            grad = IN.uv.y;   // 0 at the blade root, 1 at the tip — drives the bottom→top colour gradient
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float grad : TEXCOORD1; float3 nWS : TEXCOORD2; float3 wpos : TEXCOORD3; float fog : TEXCOORD4; };

            Varyings vert (Attributes IN)
            {
                Varyings o;
                float3 posWS, nWS; float2 uv; float grad;
                GrassVertexWS(IN, posWS, nWS, uv, grad);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = uv;
                o.grad = grad;
                o.nWS = nWS;
                o.wpos = posWS;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                float3 n = normalize(i.nWS);
                if (!IS_FRONT_VFACE(facing, true, false)) n = -n;
                float4 sc = TransformWorldToShadowCoord(i.wpos);
                Light L = GetMainLight(sc);
                // Soft half-lambert (0.35..1) so shaded sides stay lit but the lit side doesn't over-brighten.
                float ndl = saturate(dot(n, L.direction)) * 0.5 + 0.5;
                float3 sun = L.color.rgb * lerp(0.35, 1.0, ndl);
                float3 ambient = SampleSH(n) + _AmbientBoost.xxx;
                // Clamp the combined light so pale textures don't blow out to white in bright scenes,
                // THEN apply shadows to the WHOLE sum (not just the sun term): the ambient/SH floor was
                // keeping shadowed grass almost fully bright, so cloud/hill shadows didn't read on it.
                float3 lighting = min(sun + ambient, 1.25);
                lighting *= lerp(0.35, 1.0, L.shadowAttenuation);   // match how dark the shadowed terrain gets

                // Cyanilux-style root→tip gradient (white/white = plain texture colour, e.g. for moss),
                // with large-scale WORLD-noise variation: patches drift toward the DRY colours (yellowed)
                // and back to the lush base — continuous across chunks and biome borders.
                float3 bottom = _BottomColor.rgb, top = _TopColor.rgb;
                if (_ColorVariation > 0.001)
                {
                    float nse = patchNoise(i.wpos.xz * _VariationScale);
                    float dry = smoothstep(0.28, 0.72, nse) * _ColorVariation;
                    bottom = lerp(bottom, _DryBottomColor.rgb, dry);
                    top    = lerp(top,    _DryTopColor.rgb,    dry);
                }
                float3 tint = lerp(bottom, top, saturate(i.grad));

                float3 col = tex.rgb * tint * lighting;
                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Depth prepass: without this the grass is missing from _CameraDepthTexture, and every depth-based
        // effect (COZY fog post FX, SSAO, water depth blending) treats grass pixels as the BACKGROUND —
        // blades in front of the ocean were tinted with the ocean's distance fog ("ghost grass").
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            DepthVaryings DepthVert (Attributes IN)
            {
                DepthVaryings o;
                float3 posWS, nWS; float2 uv; float grad;
                GrassVertexWS(IN, posWS, nWS, uv, grad);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = uv;
                return o;
            }

            half DepthFrag (DepthVaryings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // Normals prepass (used instead of DepthOnly when SSAO/decals need normals) — same rule: the grass
        // must be present or normal-based effects see through it.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag

            struct NormalsVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 nWS : TEXCOORD1; };

            NormalsVaryings NormalsVert (Attributes IN)
            {
                NormalsVaryings o;
                float3 posWS, nWS; float2 uv; float grad;
                GrassVertexWS(IN, posWS, nWS, uv, grad);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = uv;
                o.nWS = nWS;
                return o;
            }

            half4 NormalsFrag (NormalsVaryings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                return half4(normalize(i.nWS), 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
