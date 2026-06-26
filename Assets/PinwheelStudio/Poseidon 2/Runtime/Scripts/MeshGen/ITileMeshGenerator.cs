#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon
{
    public interface ITileMeshGenerator
    {
        void Overwrite(Mesh mesh, TileMeshDesc desc);
    }
}

#endif