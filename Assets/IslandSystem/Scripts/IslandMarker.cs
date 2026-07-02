using System.Collections.Generic;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>One biome's elevation slice on an island (serialized so systems like grass can query it).</summary>
    [System.Serializable]
    public struct IslandBand
    {
        public BiomeAssetManifest biome;
        public float lo;
        public float hi;
    }

    /// <summary>
    /// Runtime tag placed on every generated island so gameplay can find hubs vs. regular islands and
    /// know which biome/type an island is. Hubs are the main large islands (cargo delivery points).
    /// </summary>
    public class IslandMarker : MonoBehaviour
    {
        public bool isHub;
        public ClimateZone climateZone;
        public IslandType islandType;
        [Tooltip("The archipelago level this island was generated for.")]
        public int level;
        [Tooltip("Biome elevation bands on this island (lo..hi normalized height), low -> high.")]
        public List<IslandBand> bands = new List<IslandBand>();
    }
}
