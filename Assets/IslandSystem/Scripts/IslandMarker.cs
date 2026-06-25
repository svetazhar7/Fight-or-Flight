using UnityEngine;

namespace IslandSystem
{
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
    }
}
