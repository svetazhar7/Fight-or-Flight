#ifndef PWETLENS_COMMON_INCLUDED
#define PWETLENS_COMMON_INCLUDED

PDECLARE_TEXTURE2D(_MainTex);
PDECLARE_TEXTURE2D(_WetLensTex);
float _Strength;

float2 DistorUV(float2 uv)
{
	float4 n0 = PSAMPLE_TEXTURE2D(_WetLensTex, uv);
	float3 n = _UnpackNormal(n0);
	n *= _Strength * 0.1;

	//fade function y=1-x^8, x[-1,1]
	float2 uvRemap = uv * 2 - 1; //remap to [-1, 1]
	float x = uvRemap.x;
	float fadeX = 1 - x * x * x * x * x * x * x * x;
	float y = uvRemap.y;
	float fadeY = 1 - y * y * y * y * y * y * y * y;
	float fade = fadeX * fadeY;
	n *= fade;

	uv += float2(-n.x, -n.y);
	return uv;
}

float4 ApplyWetLens(float2 uv)
{
	uv = saturate(DistorUV(uv));
	float4 color = PSAMPLE_TEXTURE2D(_MainTex, uv);
	return color;
}

#endif
