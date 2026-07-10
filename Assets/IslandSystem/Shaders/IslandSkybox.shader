Shader "IslandSystem/Skybox"
{
    // Procedural gradient sky for our own (COZY-free) day/night. Zenith→horizon→ground gradient, a soft sun disc +
    // glow from a global sun direction, and faint stars that fade in at night. All colours are driven per-frame by
    // the IslandSky controller (from time-of-day gradients), so this shader just paints what it's told.
    Properties
    {
        _ZenithColor  ("Zenith", Color)  = (0.20, 0.42, 0.78, 1)
        _HorizonColor ("Horizon", Color) = (0.70, 0.82, 0.92, 1)
        _GroundColor  ("Ground/Nadir", Color) = (0.18, 0.20, 0.24, 1)
        _HorizonSharp ("Horizon sharpness", Range(0.5, 8)) = 2.2
        _SunColor ("Sun Color", Color) = (1.0, 0.95, 0.85, 1)
        _SunSize  ("Sun Size", Range(0.9, 0.9999)) = 0.9986
        _SunBrightness ("Sun Brightness (HDR, feeds Bloom)", Range(1, 40)) = 14
        _SunGlow  ("Sun Glow amount", Range(0, 3)) = 0.7
        _SunGlowColor ("Sun Glow Color", Color) = (1.0, 0.75, 0.45, 1)
        _StarStrength ("Star strength (night)", Range(0, 3)) = 0.0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _SkySunDir;   // global, set by IslandSky: xyz = direction TO the sun

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor, _SunGlowColor;
                float  _HorizonSharp, _SunSize, _SunBrightness, _SunGlow, _StarStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.dir = IN.positionOS.xyz;   // skybox: object-space vertex = view direction
                return o;
            }

            float hash33 (float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }

            half4 frag (Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sun = normalize(_SkySunDir.xyz);

                // Sky gradient: ground below the horizon, horizon→zenith above (sharpened so the horizon band reads).
                float up = d.y;
                float3 col;
                if (up >= 0.0)
                {
                    float t = pow(saturate(up), 1.0 / _HorizonSharp);
                    col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);
                }
                else
                {
                    float t = pow(saturate(-up * 2.0), 0.6);
                    col = lerp(_HorizonColor.rgb, _GroundColor.rgb, t);
                }

                // Sun: a HDR disc (blooms into a bright glare) + a hot corona right around it (Bloom smears it into
                // soft rays) + a wide warm glow along the sky. The disc colour goes black at night (gradient), so
                // the HDR brightness only shows while the sun is up.
                float c = dot(d, sun);
                float disc = smoothstep(_SunSize, _SunSize + 0.0015, c);
                float corona = pow(saturate((c - (2.0 * _SunSize - 1.0)) / max(1e-4, 2.0 * (1.0 - _SunSize))), 3.0);
                float glow = pow(saturate(c * 0.5 + 0.5), 36.0) * _SunGlow;
                col += _SunColor.rgb * disc * _SunBrightness;
                col += _SunColor.rgb * corona * _SunBrightness * 0.35;
                col += _SunGlowColor.rgb * glow * saturate(up + 0.12);

                // Stars: sparse twinkles high in the sky, only when _StarStrength (night) > 0.
                if (_StarStrength > 0.001 && up > 0.02)
                {
                    float3 sp = floor(d * 260.0);
                    float st = hash33(sp);
                    float star = step(0.9975, st) * saturate(up);
                    col += star * _StarStrength;
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
