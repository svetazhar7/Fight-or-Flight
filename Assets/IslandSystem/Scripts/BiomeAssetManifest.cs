using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>Broad climate band of a biome. Used to group islands across the archipelago.</summary>
    public enum ClimateZone { Tropical, Temperate, Arid, Polar, Volcanic }

    /// <summary>Macro silhouette / landform style of an island.</summary>
    public enum IslandType { GrandCanyon, Atoll, Volcano, Plateau, Mountainous, Archipelago }

    /// <summary>How the raw noise is shaped into elevation.</summary>
    public enum HeightProfileMode
    {
        /// <summary>Plain fBm — rolling hills.</summary>
        Smooth,
        /// <summary>Ridged fBm — sharp ridgelines and eroded canyon walls.</summary>
        Ridged,
        /// <summary>Quantized into flat steps — mesas / plateaus.</summary>
        Terraced
    }

    /// <summary>Multi-octave Perlin parameters for the base heightmap.</summary>
    [Serializable]
    public struct NoiseSettings
    {
        [Tooltip("Base spatial frequency of the first octave (higher = more, smaller features).")]
        public float frequency;
        [Tooltip("Base amplitude of the first octave (relative; the field is normalized to 0..1).")]
        public float amplitude;
        [Range(1, 8)] public int octaves;

        public static NoiseSettings Default => new NoiseSettings { frequency = 3f, amplitude = 1f, octaves = 5 };
    }

    /// <summary>
    /// A condition for where on the terrain something applies: a height band (normalized 0..1) and a
    /// slope band (degrees), each with a soft fade. Shared by texture layers and object spawn rules so a
    /// designer authors "where" the same way everywhere.
    /// </summary>
    [Serializable]
    public struct PlacementCondition
    {
        [Range(0f, 1f)] public float minHeight;
        [Range(0f, 1f)] public float maxHeight;
        [Range(0f, 90f)] public float minSlope;
        [Range(0f, 90f)] public float maxSlope;

        [Tooltip("Soft fade on the height edges, in normalized height units.")]
        public float heightBlend;
        [Tooltip("Soft fade on the slope edges, in degrees.")]
        public float slopeBlend;

        public static PlacementCondition Everywhere => new PlacementCondition
        {
            minHeight = 0f, maxHeight = 1f, minSlope = 0f, maxSlope = 90f, heightBlend = 0.08f, slopeBlend = 10f
        };

        public static PlacementCondition Range(float minH, float maxH, float minS, float maxS)
            => new PlacementCondition { minHeight = minH, maxHeight = maxH, minSlope = minS, maxSlope = maxS, heightBlend = 0.08f, slopeBlend = 10f };
    }

    /// <summary>
    /// One paintable texture in the biome together with the condition under which it shows up. Bundling
    /// the texture and its condition means there are no fragile parallel arrays / layer indices.
    /// </summary>
    [Serializable]
    public class BiomeTextureLayer
    {
        public string label = "layer";
        public TerrainLayer terrainLayer;
        public PlacementCondition where = PlacementCondition.Everywhere;
        [Tooltip("Overall multiplier for this layer's contribution where it applies.")]
        [Min(0f)] public float weight = 1f;
    }

    /// <summary>
    /// A rule that scatters objects onto the terrain. Picks a random prefab from <see cref="prefabs"/>,
    /// places <see cref="count"/> of them where the condition holds, with per-instance randomization.
    /// </summary>
    [Serializable]
    public class ObjectSpawnRule
    {
        public string label = "objects";
        [Tooltip("Pool to pick from; one is chosen at random per instance.")]
        public GameObject[] prefabs = Array.Empty<GameObject>();

        [Tooltip("How many instances to attempt to place on each island.")]
        [Min(0)] public int count = 50;

        public PlacementCondition where = PlacementCondition.Range(0.1f, 1f, 0f, 30f);

        [Header("Per-instance variation")]
        [Tooltip("Uniform scale is randomized in this range.")]
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
        public bool randomYRotation = true;
        [Tooltip("Tilt the object to match the ground normal.")]
        public bool alignToNormal = false;
        [Tooltip("Push the object down into the ground so it doesn't float (world units).")]
        public float sink = 0f;

        public bool IsValid => prefabs != null && prefabs.Length > 0 && count > 0;
    }

    /// <summary>
    /// A biome is a conditional template for an island: how tall/shaped it is (height block), what it is
    /// painted with (condition-driven texture layers) and what spawns on it (condition-driven object
    /// rules). The art lists are normally filled by the "Scan Biome Folder" button; conditions are tuned
    /// here. The same template + different seeds produces different islands.
    /// </summary>
    [CreateAssetMenu(fileName = "Biome", menuName = "IslandSystem/Biome Asset Manifest")]
    public class BiomeAssetManifest : ScriptableObject
    {
        [Header("Identity")]
        public string biomeName = "Desert_GrandCanyon";
        public ClimateZone climateZone = ClimateZone.Arid;
        public IslandType islandType = IslandType.GrandCanyon;

        [Header("Height — noise")]
        public NoiseSettings noiseSettings = NoiseSettings.Default;
        public HeightProfileMode heightProfile = HeightProfileMode.Smooth;
        [Tooltip("Number of plateaus when heightProfile = Terraced.")]
        [Range(2, 32)] public int terraceSteps = 8;

        [Header("Height — shaping")]
        [Tooltip("Remaps the masked 0..1 height. Linear = no change; lift the middle for plateaus, etc.")]
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Island falloff exponent. Higher = steeper drop to the sea at the edges (~2.5 is a good start).")]
        [Range(1f, 5f)] public float islandFalloff = 2.5f;

        [Header("Terrain dimensions")]
        [Tooltip("Heightmap resolution. Must be 2^n + 1 (e.g. 257, 513, 1025).")]
        public int heightmapResolution = 513;
        [Tooltip("World-space size of the terrain (x, height, z).")]
        public Vector3 terrainSize = new Vector3(500f, 150f, 500f);

        [Header("Texturing — condition-driven layers")]
        public List<BiomeTextureLayer> textureLayers = new List<BiomeTextureLayer>();

        [Header("Objects — condition-driven spawn rules")]
        public List<ObjectSpawnRule> spawnRules = new List<ObjectSpawnRule>();

        /// <summary>The non-null TerrainLayers in author order — this is what the TerrainData uses.</summary>
        public TerrainLayer[] CollectTerrainLayers()
        {
            var list = new List<TerrainLayer>();
            if (textureLayers != null)
                foreach (var l in textureLayers)
                    if (l != null && l.terrainLayer != null) list.Add(l.terrainLayer);
            return list.ToArray();
        }
    }
}
