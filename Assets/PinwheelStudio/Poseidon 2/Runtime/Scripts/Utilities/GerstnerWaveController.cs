#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon
{
    public class GerstnerWaveController : MonoBehaviour
    {
        [SerializeField]
        protected PoseidonWaterBody m_water;
        public PoseidonWaterBody water
        {
            get
            {
                if (m_water == null)
                {
                    m_water = GetComponent<PoseidonWaterBody>();
                }
                return m_water;
            }
        }

        public Material material
        {
            get
            {
                if (water != null && water.material != null)
                {
                    return water.material;
                }
                else
                {
                    return null;
                }
            }
        }
    }
}

#endif