#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon
{
    public abstract class PlanarWaterBody : PoseidonWaterBody
    {
        public virtual void SetPlanarReflectionTexture(Texture reflectionTex)
        {
            if (m_material != null && m_material.HasProperty(PMat.REFLECTION_TEX))
            {
                m_material.SetTexture(PMat.REFLECTION_TEX, reflectionTex);
            }
        }
    }
}

#endif