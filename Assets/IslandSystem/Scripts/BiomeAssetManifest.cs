using System;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>Broad climate band of a biome. Used to group islands across the archipelago.</summary>
    public enum ClimateZone { Tropical, Temperate, Arid, Polar, Volcanic }

    /// <summary>Macro silhouette / landform style of an island.</summary>
    public enum IslandType { GrandCanyon, Atoll, Volcano, Plateau, Mountainous, Archipelago }

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
    /// One texturing rule: paints <see cref="layerIndex"/> wherever the terrain falls inside the
    /// given normalized-height band AND slope band. Edges are softened by the blend widths so layers
    /// fade into each other instead of producing hard seams.
    /// </summary>
    [Serializable]
    public struct TexturingRule
    {
        public string label;
        [Tooltip("Index into BiomeAssetManifest.terrainLayers.")]
        public int layerIndex;

        [Range(0f, 1f)] public float minHeight;
        [Range(0f, 1f)] public float maxHeight;
        [Range(0f, 90f)] public float minSlope;
        [Range(0f, 90f)] public float maxSlope;

        [Tooltip("Soft fade band on the height edges, in normalized height units.")]
        public float heightBlend;
        [Tooltip("Soft fade band on the slope edges, in degrees.")]
        public float slopeBlend;

        [Tooltip("Overall multiplier for this rule's contribution.")]
        public float weight;

        public static TexturingRule Default(int layerIndex, float minH, float maxH, float minS, float maxS)
            => new TexturingRule
            {
                label = "layer " + layerIndex,
                layerIndex = layerIndex,
                minHeight = minH, maxHeight = maxH,
                minSlope = minS, maxSlope = maxS,
                heightBlend = 0.08f, slopeBlend = 10f,
                weight = 1f
            };
    }

    /// <summary>
    /// Describes a single biome: identity, the art assets that belong to it (terrain layers + props),
    /// and the rules used to shape and texture an island of this biome. Art arrays are normally filled
    /// by the "Scan Biome Folder" button (see BiomeFolderScannerEditor), everything else is tuned here.
    /// </summary>
    [CreateAssetMenu(fileName = "Biome", menuName = "IslandSystem/Biome Asset Manifest")]
    public class BiomeAssetManifest : ScriptableObject
    {
        [Header("Identity")]
        public string biomeName = "Desert_GrandCanyon";
        public ClimateZone climateZone = ClimateZone.Arid;
        public IslandType islandType = IslandType.GrandCanyon;

        [Header("Art assets (filled by 'Scan Biome Folder')")]
        public TerrainLayer[] terrainLayers = Array.Empty<TerrainLayer>();
        public GameObject[] props = Array.Empty<GameObject>();

        [Header("Heightmap")]
        public NoiseSettings noiseSettings = NoiseSettings.Default;
        [Tooltip("Remaps the masked 0..1 height. Linear = no change; raise the middle for plateaus, etc.")]
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Island falloff exponent. Higher = steeper drop to the sea at the edges (~2.5 is a good start).")]
        [Range(1f, 5f)] public float islandFalloff = 2.5f;

        [Header("Terrain dimensions")]
        [Tooltip("Heightmap resolution. Must be 2^n + 1 (e.g. 257, 513, 1025).")]
        public int heightmapResolution = 513;
        [Tooltip("World-space size of the terrain (x, height, z).")]
        public Vector3 terrainSize = new Vector3(500f, 120f, 500f);

        [Header("Texturing rules (height + slope -> layer)")]
        public TexturingRule[] texturingRules = Array.Empty<TexturingRule>();

        [Header("Prop scatter")]
        [Range(0, 4000)] public int propCount = 0;
        [Tooltip("Props are not placed on slopes steeper than this (degrees).")]
        public float propMaxSlope = 30f;
        [Tooltip("Normalized height below which props are skipped (keeps them out of the surf).")]
        [Range(0f, 1f)] public float propMinHeight = 0.06f;
    }
}
