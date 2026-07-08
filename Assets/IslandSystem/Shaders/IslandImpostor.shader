Shader "IslandSystem/Impostor"
{
    // Camera-facing billboard that shows a distant tree from a baked HEMI-OCTAHEDRAL atlas (see TreeImpostorBaker):
    // the fragment picks the atlas cell whose baked view direction matches the current camera->tree direction, so
    // the flat quad reads as the tree from any upper-hemisphere angle — side views AND top-down (aerial). ~2 tris
    // instead of the ~2000-tri mesh. GPU-instanced; per-instance matrix carries the tree base position + scale.
    Properties
    {
        _Atlas ("Impostor Atlas", 2D) = "black" {}
        _Grid ("Atlas Grid (NxN)", Float) = 8
        _Size ("Tree Size (unit-scale bounds)", Vector) = (1,1,1,0)
        _CenterLocal ("Bounds Centre (local)", Vector) = (0,0,0,0)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.4
        _FlipV ("Flip atlas V", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Atlas); SAMPLER(sampler_Atlas);

            CBUFFER_START(UnityPerMaterial)
                float  _Grid;
                float4 _Size;
                float4 _CenterLocal;
                float  _Cutoff;
                float  _FlipV;
            CBUFFER_END

            // Inverse of TreeImpostorBaker.HemiOctaDecode: unit dir (y>=0) -> [0,1] atlas coord.
            float2 HemiOctaEncode(float3 dir)
            {
                dir /= (abs(dir.x) + abs(dir.y) + abs(dir.z));
                float2 t = float2(dir.x, dir.z);
                float2 e = float2(t.x + t.y, t.x - t.y);
                return e * 0.5 + 0.5;
            }

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 dir : TEXCOORD1; half fog : TEXCOORD2; };

            Varyings vert (Attributes IN)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(IN);
                float4x4 m = GetObjectToWorldMatrix();   // per-instance
                float3 basePos = float3(m._m03, m._m13, m._m23);
                float3 scl = float3(
                    length(float3(m._m00, m._m10, m._m20)),
                    length(float3(m._m01, m._m11, m._m21)),
                    length(float3(m._m02, m._m12, m._m22)));

                float3 center = basePos + float3(_CenterLocal.x * scl.x, _CenterLocal.y * scl.y, _CenterLocal.z * scl.z);
                float3 d = normalize(_WorldSpaceCameraPos - center);          // tree -> camera
                float3 up0 = abs(d.y) > 0.99 ? float3(0, 0, 1) : float3(0, 1, 0);
                float3 right = normalize(cross(up0, d));
                float3 up = normalize(cross(d, right));

                float w = _Size.x * max(scl.x, scl.z);
                float h = _Size.y * scl.y;
                float3 world = center + IN.positionOS.x * right * w + IN.positionOS.y * up * h;

                o.positionCS = TransformWorldToHClip(world);
                o.uv = IN.uv;
                o.dir = d;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 oct = HemiOctaEncode(normalize(i.dir));
                float2 cell = clamp(floor(oct * _Grid), 0.0, _Grid - 1.0);
                float2 quv = i.uv;
                if (_FlipV > 0.5) quv.y = 1.0 - quv.y;
                float2 uv = (quv + cell) / _Grid;
                half4 col = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, uv);
                clip(col.a - _Cutoff);
                col.rgb = MixFog(col.rgb, i.fog);
                return half4(col.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
