#if POSEIDON_2
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon
{
    //[CreateAssetMenu(menuName = "Poseidon/Water Shader Library")]
    public class WaterShaderLibrary : ScriptableObject
    {
        public static WaterShaderLibrary instance { get; private set; }

        [SerializeField]
        internal List<ShaderDesc> m_shaderDescriptions;

        private void OnValidate()
        {
            for (int i = 0; i < m_shaderDescriptions.Count; ++i)
            {
                m_shaderDescriptions[i].SetNameFromFeatures((i + 1).ToString());
            }
        }
    }
}

#endif