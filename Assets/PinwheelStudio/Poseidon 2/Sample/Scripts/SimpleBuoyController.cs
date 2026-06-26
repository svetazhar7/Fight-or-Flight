#if POSEIDON_2
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Pinwheel.Poseidon;

[ExecuteInEditMode]
public class SimpleBuoyController : MonoBehaviour
{
    public PoseidonWaterBody water;

    public void Update()
    {
        if (water == null || water.material == null)
            return;
        Vector3 positionWS = transform.position;
        positionWS.y = water.transform.position.y;

        if (PMat.HasRipplesFX(water.material))
        {
            WaterUtilities.RippleParams rippleParams = WaterUtilities.ExtractRippleParams(water.material);
            positionWS += WaterUtilities.CalculateRippleOffsetWS(positionWS, rippleParams);
        }

        if (PMat.HasWaveFX(water.material))
        {
            WaterUtilities.WaveParams waveParams = WaterUtilities.ExtractWaveParams(water.material);
            Vector3 waveOffset = WaterUtilities.CalculateWaveOffsetWS(positionWS, waveParams);

            if (PMat.HasWaveMaskFX(water.material))
            {
                WaterUtilities.WaveMaskParams waveMaskParams = WaterUtilities.ExtractWaveMaskParams(water.material);
                waveOffset = WaterUtilities.ApplyWaveMask(positionWS, waveOffset, waveMaskParams);
            }

            positionWS += waveOffset;
        }

        transform.position = positionWS;
    }
}

#endif