Shader "IslandSystem/Skybox"
{
    // Procedural gradient sky for our own (COZY-free) day/night. Zenith→horizon→ground gradient plus the layered
    // sun stack driven by the Sun System modules: a soft disc with limb darkening (SunDiscModule), a two-radius
    // halo ring + wide directional glow (SunHaloModule), horizon glow and low-sky haze (SunEnvironmentModule),
    // and faint stars for the night. All colours/params are pushed from C# — this shader just paints what it's told.
    Properties
    {
        _ZenithColor  ("Zenith", Color)  = (0.20, 0.42, 0.78, 1)
        _HorizonColor ("Horizon", Color) = (0.70, 0.82, 0.92, 1)
        _GroundColor  ("Ground/Nadir", Color) = (0.18, 0.20, 0.24, 1)
        _HorizonSharp ("Horizon sharpness", Range(0.5, 8)) = 2.2

        [Header(Sun disc)]
        _SunColor ("Sun Color", Color) = (1.0, 0.95, 0.85, 1)
        _SunSize  ("Sun Size", Range(0.9, 0.9999)) = 0.9975
        _SunDiscSoft ("Sun disc edge softness", Range(0.0002, 0.05)) = 0.006
        _SunLimb ("Sun limb darkening", Range(0, 1)) = 0.35
        _SunRimGlow ("Sun rim corona", Range(0, 1)) = 0.5
        _SunBrightness ("Sun Brightness (HDR, feeds Bloom)", Range(1, 40)) = 11

        [Header(Halo)]
        _HaloColor ("Halo Color", Color) = (1.0, 0.83, 0.6, 1)
        _HaloParams ("Halo (cosInner, cosOuter, softness, falloff)", Vector) = (0.9997, 0.97, 0.65, 2.2)
        _HaloIntensity ("Halo Intensity", Range(0, 5)) = 0.8

        [Header(Wide glow)]
        _SunGlow  ("Sun Glow amount", Range(0, 3)) = 0.55
        _SunGlowColor ("Sun Glow Color", Color) = (1.0, 0.75, 0.45, 1)
        _SunGlowExp ("Sun Glow exponent", Range(4, 128)) = 30

        [Header(Atmosphere)]
        _HorizonGlowColor ("Horizon Glow Color", Color) = (1.0, 0.85, 0.65, 1)
        _HorizonGlowParams ("Horizon Glow (strength, height, -, -)", Vector) = (0.5, 0.18, 0, 0)
        _HazeColor ("Haze Color", Color) = (1.0, 0.92, 0.8, 1)
        _HazeParams ("Haze (density, height, sunSideScatter, -)", Vector) = (0.4, 0.3, 1.0, 0)

        [Header(Night)]
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

            float4 _SkySunDir;   // global, set by SunSystem: xyz = direction TO the sun

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _HorizonColor, _GroundColor;
                float4 _SunColor, _SunGlowColor, _HaloColor, _HaloParams;
                float4 _HorizonGlowColor, _HorizonGlowParams, _HazeColor, _HazeParams;
                float  _HorizonSharp, _SunSize, _SunDiscSoft, _SunLimb, _SunRimGlow, _SunBrightness;
                float  _HaloIntensity, _SunGlow, _SunGlowExp, _StarStrength;
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

                // ---- Base gradient: ground below the horizon, horizon→zenith above ----
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

                // How aligned this pixel is with the sun's compass direction (for one-sided atmosphere effects).
                float2 dH = normalize(d.xz + 1e-5);
                float2 sH = normalize(sun.xz + 1e-5);
                float sunSide = pow(saturate(dot(dH, sH) * 0.5 + 0.5), 2.0);

                // ---- Haze: warm veil pooling over the lower sky, brighter on the sun's side ----
                float hz = saturate(1.0 - max(up, 0.0) / max(_HazeParams.y, 0.02));
                hz = hz * hz * saturate(_HazeParams.x);
                float3 hazeCol = lerp(_HazeColor.rgb, _SunGlowColor.rgb, sunSide * saturate(_HazeParams.z) * 0.5);
                col = lerp(col, hazeCol, hz);

                // ---- Horizon glow: a thin luminous band hugging the horizon line ----
                float hg = exp(-abs(up) / max(_HorizonGlowParams.y, 0.02)) * _HorizonGlowParams.x;
                col += _HorizonGlowColor.rgb * hg * lerp(0.3, 1.0, sunSide);

                // ---- Sun stack: soft disc with limb darkening, rim corona, halo ring, wide glow ----
                float c = dot(d, sun);

                float disc = smoothstep(_SunSize - _SunDiscSoft, _SunSize + _SunDiscSoft, c);
                float rim = saturate((c - _SunSize) / max(1.0e-5, 1.0 - _SunSize));       // 0 at edge → 1 at centre
                float limb = lerp(1.0, 0.35 + 0.65 * pow(rim, 0.7), _SunLimb);            // volumetric limb falloff
                col += _SunColor.rgb * disc * _SunBrightness * limb;

                float corona = pow(saturate((c - (2.0 * _SunSize - 1.0)) / max(1.0e-4, 2.0 * (1.0 - _SunSize))), 3.0);
                col += _SunColor.rgb * corona * _SunBrightness * 0.35 * _SunRimGlow;

                // Halo ring between the two cosine radii, soft-blendable falloff.
                float haloT = saturate((c - _HaloParams.y) / max(1.0e-5, _HaloParams.x - _HaloParams.y));
                float haloSharp = pow(haloT, _HaloParams.w);
                float haloSoft = haloT * haloT * (3.0 - 2.0 * haloT);
                float halo = lerp(haloSharp, haloSoft, saturate(_HaloParams.z));
                col += _HaloColor.rgb * halo * _HaloIntensity;

                // Wide directional glow washing along the sky towards the sun.
                float glow = pow(saturate(c * 0.5 + 0.5), _SunGlowExp) * _SunGlow;
                col += _SunGlowColor.rgb * glow * saturate(up + 0.12);

                // ---- Stars: sparse twinkles high in the sky, only when _StarStrength (night) > 0 ----
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
