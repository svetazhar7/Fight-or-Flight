using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>One biome (zone) plus the share of the island it should occupy.</summary>
    [Serializable]
    public class WeightedBiome
    {
        public BiomeAssetManifest biome;
        [Tooltip("Spawn share of this biome on the island, in percent. The list is normalized at use time.")]
        [Range(0f, 100f)] public float percent = 10f;
    }

    /// <summary>
    /// Defines one island TYPE (e.g. Polar Archipelago): the overall island shape plus the biomes that
    /// compose it and in what proportion. A biome is a sub-zone (Beach, Plain, Hills, ...) that is unique
    /// to this type — "Beach" here is a different asset than "Beach" of another type. The percentages are
    /// honoured by laying out biomes in elevation bands sized by their share of the island's land area
    /// (ordered by each biome's <see cref="BiomeAssetManifest.elevationOrder"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "IslandType", menuName = "IslandSystem/Island Type Definition")]
    public class IslandTypeDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Human-readable name, e.g. \"Полярный архипелаг\".")]
        public string displayName;
        public ClimateZone climateZone;
        public IslandType islandType;

        [Header("Island shape")]
        public NoiseSettings noiseSettings = NoiseSettings.Default;
        public HeightProfileMode heightProfile = HeightProfileMode.Smooth;
        [Range(2, 32)] public int terraceSteps = 8;
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Range(1f, 5f)] public float islandFalloff = 2.3f;
        [Tooltip("Heightmap resolution. Must be 2^n + 1 (e.g. 257, 513, 1025).")]
        public int heightmapResolution = 513;
        [Tooltip("World-space size of the terrain (x, height, z).")]
        public Vector3 terrainSize = new Vector3(500f, 150f, 500f);

        [Header("Relief & erosion (biome-aware)")]
        [Tooltip("Master amount of rugged relief added in highland/mountain biomes (normalized height). " +
                 "0 = flat everywhere; the per-biome reliefStrength × elevationOrder scales it locally.")]
        [Range(0f, 1f)] public float mountainRelief = 0.85f;
        [Tooltip("Spatial frequency of the eroded ridge detail (higher = finer, more numerous ridges).")]
        public float reliefFrequency = 11f;
        [Tooltip("Octaves of the eroded ridge detail.")]
        [Range(1, 7)] public int reliefOctaves = 6;
        [Tooltip("Erosion bias: how much the ridge detail digs valleys vs. only raising peaks (0..1). " +
                 "Higher also flattens valley floors more strongly (ridge-multifractal valley gating).")]
        [Range(0f, 1f)] public float erosionStrength = 0.6f;
        [Tooltip("How hard low-relief (plain/beach) ground is smoothed flat. 0 = keep raw noise, 1 = very flat.")]
        [Range(0f, 1f)] public float plainsFlatten = 0.85f;
        [Tooltip("Subtle roughness kept everywhere (incl. plains) so flat ground isn't glassy (normalized).")]
        [Range(0f, 0.05f)] public float microRoughness = 0.005f;

        [Header("Composition — biomes by % (sums to 100)")]
        public List<WeightedBiome> biomes = new List<WeightedBiome>();

        /// <summary>Sum of all valid biome percentages (ideally 100).</summary>
        public float TotalPercent
        {
            get
            {
                float t = 0f;
                if (biomes != null)
                    foreach (var b in biomes)
                        if (b != null && b.biome != null) t += b.percent;
                return t;
            }
        }
    }
}
