#ifndef OVERHEAD_CLOUD_INCLUDED
#define OVERHEAD_CLOUD_INCLUDED

#include "JCommon.cginc"

void CalculateOverheadCloudColor(
	float4 localPos,
	fixed4 cloudColor,
	fixed cloudAltitude,
	fixed cloudSize, fixed cloudStep,
	fixed animationSpeed,
	fixed flowSpeed,
	fixed flowX, fixed flowZ,
	fixed remapMin, fixed remapMax,
	out fixed4 color)
{
	fixed3 rayDir = localPos.xyz;
	float3 cloudPlaneOrigin = float3(0, 1000, 0);
	float3 cloudPlaneNormal = float3(0, 1, 0);

	float rayLength = dot(cloudPlaneOrigin, cloudPlaneNormal) / (dot(rayDir, cloudPlaneNormal) + 0.000001);
	float3 intersectionPoint = rayDir * rayLength - float3(1, 0, 1) * 1 * _JupiterTime;

	fixed loop = 2;

	fixed noise = 0;
	fixed sample = 0;
	fixed noiseSize = 5 * 1000 + 0.0001;
	fixed noiseAmp = 1;
	fixed sign = -1;
	fixed2 span = fixed2(1, 1) * 20 * _JupiterTime * 0.0001;
	for (fixed i = 0; i < loop; ++i)
	{
		sample = CloudTexLod0((intersectionPoint.xz) / noiseSize + sign * span) * noiseAmp;
		noise += sample;
		noiseSize *= 0.5;
		noiseAmp *= 0.5;
		sign *= -1;
	}
	noise = noise * 0.5 + 0.5;
	noise = noise * 0.8;
	noise = lerp(0, 2, noise);
	noise = saturate(noise);

	color = fixed4(cloudColor.rgb, cloudColor.a * noise);

	float sqrCloudDiscRadius = 100000000;
	float sqrDistanceToCloudPlaneOrigin = SqrDistance(intersectionPoint, cloudPlaneOrigin);

	fixed fDistance = saturate(1 - InverseLerpUnclamped(0, sqrCloudDiscRadius, sqrDistanceToCloudPlaneOrigin));
	color.a *= fDistance * fDistance;

	fixed4 clear = fixed4(0, 0, 0, 0);
	color = lerp(clear, color, localPos.y > 0);
}

#endif