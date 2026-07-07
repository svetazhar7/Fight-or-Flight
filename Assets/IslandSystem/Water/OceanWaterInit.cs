using UnityEngine;
using Pinwheel.Poseidon;

namespace IslandSystem
{
    /// <summary>
    /// Regenerates the ocean's Poseidon <see cref="TileableWater"/> tile meshes ONCE, a frame after they were
    /// created. Calling <c>GenerateMesh()</c> in the same frame the component is added (which is what happens
    /// during archipelago generation) produces EMPTY tiles — Poseidon's own init hasn't run yet — so the sea has
    /// no geometry. Running it from the first Update (after init) fixes that for both editor generation and the
    /// runtime/host path. Re-runs after a domain reload too (the generated meshes aren't serialized).
    /// </summary>
    [ExecuteAlways]
    public class OceanWaterInit : MonoBehaviour
    {
        bool _done;

        void OnEnable() => _done = false;

        void Update()
        {
            if (_done) return;
            _done = true;
            foreach (var tw in GetComponentsInChildren<TileableWater>(true))
                if (tw != null) tw.GenerateMesh();
        }
    }
}
