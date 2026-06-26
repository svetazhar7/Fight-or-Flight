Shader "Hidden/Poseidon/WaveMaskVisualizer"
{
	Properties
	{
		_WaveMask("Texture", 2D) = "white" { }
	}
	SubShader
	{
		Tags { "RenderType" = "Transparent" "Queue" = "Overlay" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		ZTest Always
		LOD 100

		Pass
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma shader_feature_local CREST
			#pragma shader_feature_local HEIGHT

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex: POSITION;
				float2 uv: TEXCOORD0;
			};

			struct v2f
			{
				float2 uv: TEXCOORD0;
				float4 pos: SV_POSITION;
				float4 screenPos: TEXCOORD1;
			};

			sampler2D _WaveMask;

			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.screenPos = ComputeScreenPos(o.pos);
				return o;
			}

			fixed4 frag(v2f i): SV_Target
			{
				float2 screenPos = i.screenPos.xy / i.screenPos.w;
				float2 pixel = screenPos.xy * _ScreenParams.xy;
				//pixel = floor(pixel/4);

				if (floor(pixel.x) % 3 == 0 || floor(pixel.y) % 3 == 0)
				{
					discard;
				}

				fixed4 color = 0;
				fixed4 mask = tex2D(_WaveMask, i.uv);
				#if HEIGHT
					color = fixed4(1, 0, 0, mask.r);
				#endif
				#if CREST
					color = fixed4(0, 1, 0, mask.g);
				#endif

				return color;
			}
			ENDCG
		}
	}
}
