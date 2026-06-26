#if POSEIDON_2
#if POSEIDON_2_URP
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace Pinwheel.Poseidon.FX.URP
{
    [System.Serializable]
    [VolumeComponentMenu("Poseidon/Underwater")]
    public class UnderwaterUrp : VolumeComponent
#if UNITY_6000_0_OR_NEWER
        , IPostProcessComponent
#endif
    {
        [Header("Water Body")]
        public FloatParameter maxDepth = new FloatParameter(10);
        [Range(0f, 3f)]
        public FloatParameter surfaceColorBoost = new FloatParameter(1);

        [Header("Fog")]
        public ColorParameter shallowFogColor = new ColorParameter(new Color(0, 0, 1, 0.5f));
        public ColorParameter deepFogColor = new ColorParameter(new Color(0, 0, 1, 1));
        public FloatParameter viewDistance = new FloatParameter(40);

        [Header("Caustic")]
        public TextureParameter noiseTexture = new TextureParameter(null);
        public TextureParameter causticTexture = new TextureParameter(null);
        public FloatParameter causticSize = new FloatParameter(10);
        [Range(0f, 1f)]
        public FloatParameter causticStrength = new FloatParameter(1);

        [Header("Distortion")]
        public TextureParameter distortionNormalMap = new TextureParameter(null);
        public FloatParameter distortionStrength = new FloatParameter(1);
        public FloatParameter waterFlowSpeed = new FloatParameter(1);

        [Header("Internal")]
        [Range(0f, 1f)]
        public FloatParameter waterLevel = new FloatParameter(0);
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