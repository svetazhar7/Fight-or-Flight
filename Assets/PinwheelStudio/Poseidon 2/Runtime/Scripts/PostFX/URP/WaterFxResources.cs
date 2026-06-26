#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon.FX
{
    [CreateAssetMenu(menuName = "Poseidon/Water Fx Resources")]
    public class WaterFxResources : ScriptableObject
    {
        public Material underwaterMaterial;
        public Material wetLensMaterial;
    }
}

#endif