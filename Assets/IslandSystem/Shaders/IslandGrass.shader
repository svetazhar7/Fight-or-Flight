Shader "IslandSystem/Grass"
{
    // GPU-instanced foliage (URP), Cyanilux "GPU Instanced Grass" style — the full INDIRECT setup: drawn with
    // Graphics.RenderMeshIndirect; a compute shader (GrassFrustumCull.compute) appends the ids of instances
    // inside the camera frustum to _VisibleIDs, so SV_InstanceID indexes _VisibleIDs first, then the visible id
    // fetches the per-instance WORLD matrix from _PerInstanceData (no unity_ObjectToWorld, no 1023-array limit).
    // Wind sway + player flatten per-vertex (scaled by object-space height so the root is pinned and the tips
    // sway). Alpha-cutout texture, no colour tint (uses the prefab texture directly).
    Properties
    {
        [MainTexture] _MainTex ("Texture (RGBA)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.3
        _BottomColor ("Bottom Color (root tint)", Color) = (1,1,1,1)
        _TopColor    ("Top Color (tip tint)",     Color) = (1,1,1,1)
        _Tiles       ("Texture Tiles (NxN atlas, 1 = off)", Float) = 1
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            #define MAX_GRASS_INTERACTORS 16
            float4 _GrassInteractors[MAX_GRASS_INTERACTORS]; // xyz world pos, w radius
            int    _GrassInteractorCount;

            StructuredBuffer<float4x4> _PerInstanceData;     // per-instance WORLD matrices (ALL instances of the chunk)
            StructuredBuffer<uint>     _VisibleIDs;          // ids that survived GPU frustum culling (GrassFrustumCull.compute)
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BottomColor, _TopColor;
                float  _Cutoff, _Tiles;
                float  _WindHeight, _WindStrength, _WindSpeed, _WindScale, _BendStrength, _AmbientBoost;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; uint instanceID : SV_InstanceID; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float grad : TEXCOORD1; float3 nWS : TEXCOORD2; float3 wpos : TEXCOORD3; float fog : TEXCOORD4; };

            Varyings vert (Attributes IN)
            {
                Varyings o;
                uint visId = _VisibleIDs[IN.instanceID];               // indirect draw: instanceID walks the VISIBLE list
                float4x4 m = _PerInstanceData[visId];                  // this instance's world matrix
                float3 posWS = mul(m, float4(IN.positionOS.xyz, 1.0)).xyz;
                float3 nWS   = normalize(mul(m, float4(IN.normalOS, 0.0)).xyz);

                // Bend from OBJECT-space height (root = 0, tip = 1) — root is pinned, only the tips sway.
                float bend = saturate(IN.positionOS.y / max(0.01, _WindHeight));

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

                o.positionCS = TransformWorldToHClip(posWS);

                // Cyanilux flipbook: the texture is an NxN atlas of blade-clump variants; each instance picks a
                // random tile. visId is STABLE per instance (indexes the persistent matrix buffer), so the tile
                // doesn't flicker when frustum culling reshuffles SV_InstanceID. Y scaled by 0.9 so the tile
                // above doesn't leak through at the blade tips (the tutorial's Tiling-And-Offset trick).
                float2 uv = TRANSFORM_TEX(IN.uv, _MainTex);
                if (_Tiles > 1.5)
                {
                    uint n = (uint)_Tiles;
                    uint h = visId * 747796405u + 2891336453u;
                    h = ((h >> ((h >> 28) + 4u)) ^ h) * 277803737u;
                    h = (h >> 22) ^ h;
                    uint tile = h % (n * n);
                    uv = (IN.uv * float2(1.0, 0.9) + float2(tile % n, tile / n)) / n;
                }
                o.uv = uv;
                o.grad = IN.uv.y;   // 0 at the blade root, 1 at the tip — drives the bottom→top colour gradient
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
                float3 diffuse = L.color.rgb * lerp(0.35, 1.0, ndl) * L.shadowAttenuation;
                float3 ambient = SampleSH(n) + _AmbientBoost.xxx;
                // Cyanilux-style root→tip gradient (white/white = plain texture colour, e.g. for moss).
                float3 tint = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(i.grad));
                // Clamp the combined light so pale textures don't blow out to white in bright scenes.
                float3 col = tex.rgb * tint * min(diffuse + ambient, 1.25);
                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
