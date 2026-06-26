#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Poseidon.FX;
#if UNITY_POST_PROCESSING_STACK_V2
using UnityEngine.Rendering.PostProcessing;
#endif


namespace Pinwheel.Poseidon.FX.BiRP
{
    [AddComponentMenu("Water (Poseidon)/Water FX Controller BiRP")]
    public class WaterFxControllerBirp
#if UNITY_POST_PROCESSING_STACK_V2
        : WaterFxControllerBase
    {
        [SerializeField]
        protected PostProcessProfile m_postProcessProfile;
        public PostProcessProfile postProcessProfile
        {
            get
            {
                return m_postProcessProfile;
            }
            set
            {
                m_postProcessProfile = value;
            }
        }

        [SerializeField]
        protected Shader m_underwaterShader;
        [SerializeField]
        protected Shader m_wetLensShader;

        public static Shader underwaterShader { get; internal set; }
        public static Shader wetLensShader { get; internal set; }

        protected override void Start()
        {
            base.Start();
            underwaterShader = m_underwaterShader;
            wetLensShader = m_wetLensShader;
        }

        protected override void AddVolumeComponentToObject(GameObject go)
        {
            PostProcessVolume v = go.AddComponent<PostProcessVolume>();
            v.sharedProfile = m_postProcessProfile;
            v.isGlobal = false;
        }

        protected override void UpdateUnderwaterFX(float intensity, float waterLevelWS)
        {
            if (m_postProcessProfile == null)
                return;
            UnderwaterBirp underwaterSettings;
            if (m_postProcessProfile.TryGetSettings<UnderwaterBirp>(out underwaterSettings))
            {
                underwaterSettings.intensity.Override(intensity);
                underwaterSettings.waterLevel.Override(waterLevelWS);
            }
        }

        protected override void UpdateWetLensFX(float intensity)
        {
            if (m_postProcessProfile == null)
                return;
            WetLensBirp wetlensSettings;
            if (m_postProcessProfile.TryGetSettings<WetLensBirp>(out wetlensSettings))
            {
                wetlensSettings.intensity.Override(intensity);
            }
        }
    }
#else
    : MonoBehaviour
    {}
#endif
}

#endif