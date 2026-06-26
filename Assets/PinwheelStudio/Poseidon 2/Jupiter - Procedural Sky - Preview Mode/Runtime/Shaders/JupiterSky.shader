Shader "Jupiter/Sky"
{
	Properties
	{
		[HideInInspector] _SkyColor("Sky Color", Color) = (0.15, 0.4, 0.65, 1)
		[HideInInspector] _HorizonColor("Horizon Color", Color) = (1, 1, 1, 1)
		[HideInInspector] _GroundColor("Ground Color", Color) = (0.4, 0.4, 0.4, 1)
		[HideInInspector] _HorizonThickness("Horizon Thickness", Range(0.0, 1.0)) = 0.3
		[HideInInspector] _HorizonExponent("Horizon Exponent", Float) = 1.0

		[HideInInspector] _SunColor("Sun Color", Color) = (1, 1, 1, 1)
		[HideInInspector] _SunSize("Sun Size", Float) = 0.1
		[HideInInspector] _SunSoftEdge("Sun Soft Edge", Float) = 0
		[HideInInspector] _SunGlow("Sun Glow", Float) = 0
		[HideInInspector] _SunDirection("Sun Direction", Vector) = (-1, -1, -1, 0)
		[HideInInspector] _SunLightColor("Sun Light Color", Color) = (1,1,1,1)
		[HideInInspector] _SunLightIntensity("Sun Light Intensity", Float) = 1

		[HideInInspector] _OverheadCloudColor("Overhead Cloud Color", Color) = (1,1,1,1)
	}
		SubShader
		{
			Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
			Cull Off ZWrite Off
			LOD 100

			Pass
			{
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile_instancing

				#pragma shader_feature_local SUN
				#pragma shader_feature_local OVERHEAD_CLOUD

				#include "UnityCG.cginc"
				#include "./CGIncludes/JCommon.cginc"
				#include "./CGIncludes/JSkyGradient.cginc"
				#include "./CGIncludes/JSunMoon.cginc"			
				#include "./CGIncludes/JOverheadCloud.cginc"

				struct appdata
				{
					float4 vertex : POSITION;
					float2 uv : TEXCOORD0;
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct v2f
				{
					float2 uv : TEXCOORD0;
					float4 vertex : SV_POSITION;
					float4 localPos : TEXCOORD1;
					UNITY_VERTEX_OUTPUT_STEREO
				};

				uniform fixed4 _SkyColor;
				uniform fixed4 _HorizonColor;
				uniform fixed4 _GroundColor;
				uniform fixed _HorizonThickness;
				uniform fixed _HorizonExponent;

				#if SUN				
					uniform fixed4 _SunColor;
					uniform float _SunSize;
					uniform fixed _SunSoftEdge;
					uniform fixed _SunGlow;
					uniform float4 _SunDirection;
					uniform float4 _SunLightColor;
					uniform float4 _SunLightIntensity;
				#endif //SUN

				#if OVERHEAD_CLOUD
					uniform fixed4 _OverheadCloudColor;
				#endif


				v2f vert(appdata v)
				{
					v2f o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

					o.vertex = UnityObjectToClipPos(v.vertex);
					o.uv = v.uv;
					o.localPos = v.vertex;
					return o;
				}

				fixed4 frag(v2f i) : SV_Target
				{
					float4 localPos = float4(i.localPos.xyz, 1);
					//localPos = mul(_SkyTransform, localPos);
					float4 normalizedLocalPos = float4(normalize(localPos.xyz), 1);
					fixed4 color = fixed4(0,0,0,0);

					fixed4 skyBlendColor;
					fixed4 horizonBlendColor;
					CalculateSkyGradientColor(
						normalizedLocalPos,
						_SkyColor, _HorizonColor, _GroundColor,
						_HorizonThickness, _HorizonExponent, 0,
						skyBlendColor, horizonBlendColor);
					color = BlendOverlay(skyBlendColor, color);

					#if SUN
						fixed4 sunColor;							
						CalculateSunMoonColor(
							normalizedLocalPos,
							_SunColor,
							_SunSize, _SunSoftEdge, _SunGlow,
							_SunDirection,
							sunColor);
						color = BlendOverlay(sunColor, color);
					#endif //SUN

					#if OVERHEAD_CLOUD
						fixed4 overheadCloudColor;
						CalculateOverheadCloudColor(
							normalizedLocalPos,
							_OverheadCloudColor,
							1,
							1,
							1,
							1,
							1,
							1,
							1,
							1,
							1,
							overheadCloudColor);
						color = BlendOverlay(overheadCloudColor, color);
					#endif

					color = BlendOverlay(horizonBlendColor, color);

					//color = float4(localPos.xyz, 1);
					return color;
				}
				ENDCG
			}
	}
		Fallback "Unlit/Color"
}
