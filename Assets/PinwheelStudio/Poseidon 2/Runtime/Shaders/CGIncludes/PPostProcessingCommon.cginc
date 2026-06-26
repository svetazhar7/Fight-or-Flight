#ifndef PPOSTPROCESSING_COMMON_INCLUDED
#define PPOSTPROCESSING_COMMON_INCLUDED
#include "PPostProcessingDefines.cs.cginc"

#if defined (POSEIDON_SRP)

#define PDECLARE_TEXTURE2D(textureName) TEXTURE2D_X(textureName)
#define PSAMPLE_TEXTURE2D(textureName, uv) SAMPLE_TEXTURE2D_X(textureName, sampler_LinearRepeat, uv)

#define PDECLARE_DEPTH_TEXTURE PDECLARE_TEXTURE2D(_CameraDepthTexture)
#define PLINEAR_EYE_DEPTH(uv) LinearEyeDepth(PSAMPLE_TEXTURE2D(_CameraDepthTexture, uv).r, _ZBufferParams)

#else //Builtin RP

#define PDECLARE_TEXTURE2D(textureName) TEXTURE2D_SAMPLER2D(textureName, sampler##textureName)
#define PSAMPLE_TEXTURE2D(textureName, uv) SAMPLE_TEXTURE2D(textureName, sampler##textureName, uv)

#define PDECLARE_DEPTH_TEXTURE PDECLARE_TEXTURE2D(_CameraDepthTexture)
#define PLINEAR_EYE_DEPTH(uv) LinearEyeDepth(PSAMPLE_TEXTURE2D(_CameraDepthTexture, uv).r)

#endif


inline float3 _UnpackNormalDXT5nm(float4 packednormal)
{
	float3 normal;
	normal.xy = packednormal.wy * 2 - 1;
	normal.z = sqrt(1 - saturate(dot(normal.xy, normal.xy)));
	return normal;
}

// Unpack normal as DXT5nm (1, y, 1, x) or BC5 (x, y, 0, 1)
// Note neutral texture like "bump" is (0, 0, 1, 1) to work with both plain RGB normal and DXT5nm/BC5
float3 _UnpackNormalmapRGorAG(float4 packednormal)
{
	// This do the trick
	packednormal.x *= packednormal.w;

	float3 normal;
	normal.xy = packednormal.xy * 2 - 1;
	normal.z = sqrt(1 - saturate(dot(normal.xy, normal.xy)));
	return normal;
}
inline float3 _UnpackNormal(float4 packednormal)
{
#if defined(UNITY_NO_DXT5nm)
	return packednormal.xyz * 2 - 1;
#elif defined(UNITY_ASTC_NORMALMAP_ENCODING)
	return _UnpackNormalDXT5nm(packednormal);
#else
	return _UnpackNormalmapRGorAG(packednormal);
#endif
}


#endif
