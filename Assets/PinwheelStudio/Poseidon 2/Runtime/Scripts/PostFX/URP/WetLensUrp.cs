#if POSEIDON_2
#if POSEIDON_2_URP
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace Pinwheel.Poseidon.FX.URP
{
    [System.Serializable]
    [VolumeComponentMenu("Poseidon/Wet Lens")]
    public class WetLensUrp : VolumeComponent
#if UNITY_6000_0_OR_NEWER
        , IPostProcessComponent
#endif
    {
        [Header("Lens Distort")]
        public TextureParameter dropletsNormalMap = new TextureParameter(null);
        [Range(0f, 1f)]
        public FloatParameter strength = new FloatParameter(1);

        [Header("Internal")]
        [Range(0f, 1f)]
        public FloatParameter intensity = new FloatParameter(0);

#if UNITY_6000_0_OR_NEWER
        public bool IsActive()
        {
            return active && intensity.value > 0;
        }
#endif
    }
}
#endif

#endif