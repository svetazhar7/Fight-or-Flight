#if POSEIDON_2
#if POSEIDON_2_URP
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Poseidon.FX;
using UnityEngine.Rendering;

namespace Pinwheel.Poseidon.FX.URP
{
    [AddComponentMenu("Water (Poseidon)/Water FX Controller URP")]
    public class WaterFxControllerUrp : WaterFxControllerBase
    {
        [SerializeField]
        protected VolumeProfile m_volumeProfile;
        public VolumeProfile volumeProfile
        {
            get
            {
                return m_volumeProfile;
            }
            set
            {
                m_volumeProfile = value;
            }
        }

        protected override void UpdateUnderwaterFX(float intensity, float waterLevelWS)
        {
            if (m_volumeProfile == null)
                return;
            UnderwaterUrp underwaterSettigns;
            if (m_volumeProfile.TryGet<UnderwaterUrp>(out underwaterSettigns))
            {
                underwaterSettigns.intensity.Override(intensity);
                underwaterSettigns.waterLevel.Override(waterLevelWS);
            }
        }

        protected override void UpdateWetLensFX(float intensity)
        {
            if (m_volumeProfile == null)
                return;
            WetLensUrp wetlensSettings;
            if (m_volumeProfile.TryGet<WetLensUrp>(out wetlensSettings))
            {
                wetlensSettings.intensity.Override(intensity);
            }
        }

        protected override void AddVolumeComponentToObject(GameObject go)
        {
            Volume v = go.AddComponent<Volume>();
            v.sharedProfile = volumeProfile;
            v.isGlobal = false;
        }
    }
}
#endif

#endif