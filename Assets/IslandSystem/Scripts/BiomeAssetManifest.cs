using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>Broad climate band an island belongs to. Each <see cref="IslandType"/> lives in one zone.</summary>
    public enum ClimateZone { Cold, Hot, Temperate, Tropical }

    /// <summary>
    /// Landform / theme of an island, grouped by the <see cref="ClimateZone"/> it belongs to. These are
    /// island TYPES (the kind of place), not biomes — a biome (template) references one of these as a tag.
    /// See <see cref="IslandTaxonomy"/> for the climate-of / types-in / display-name mappings.
    /// </summary>
    public enum IslandType
    {
        // --- Cold ---
        PolarArchipelago,   // Полярный архипелаг
        PolarCanyons,       // Ледяные каньоны
        GlacialCoast,       // Ледниковое побережье
        BorealTaiga,        // Хвойная тайга
        Tundra,             // Тундровые острова
        GlacierHighlands,   // Горные ледники

        // --- Tropical ---
        ParadiseIslands,    // Райские острова
        MangroveCoast,      // Мангровое побережье
        TropicalHighlands,  // Тропические нагорья
        BambooIsles,        // Бамбуковые острова
        RockyTropicalCoast, // Скалистые тропические берега
        RainforestIslands,  // Дождевые леса

        // --- Hot ---
        ClassicDesert,      // Классическая пустыня
        GrandCanyons,       // Каньоны
        WildWest,           // Дикий Запад
        RedRockBadlands,    // Красные скалы
        LavaWastelands,     // Лавовые пустоши
        RockPlateau,        // Каменистое плато

        // --- Temperate ---
        EuropeanCountryside,  // Европейская сельская местность
        SlavicWilderness,     // Славянская глубинка
        LakeDistrict,         // Озёрный край
        RollingHighlands,     // Холмистые острова
        MountainValleys,      // Горные долины
        ConiferousArchipelago,// Хвойный архипелаг
        MixedWoodlands        // Смешанные леса
    }

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
        [Tooltip("Pool to pick from; one is chosen per instance. For trees this is the SPECIES list — put " +
                 "several tree prefabs here and weight their mix with prefabWeights.")]
        public GameObject[] prefabs = Array.Empty<GameObject>();

        [Tooltip("Optional selection weights, parallel to prefabs (the species ratio). Empty/all-zero = equal. " +
                 "E.g. prefabs [Pine, Birch] with weights [70, 30] => ~70% pine, 30% birch.")]
        public float[] prefabWeights = Array.Empty<float>();

        [Tooltip("How many instances to attempt to place on each island. Used by rocks/props.")]
        [Min(0)] public int count = 50;

        [Tooltip("TREES: density in instances per 100 m² of eligible ground. The spacing between trees is " +
                 "derived from this (≈ 10/sqrt(density) metres). Used instead of count for tree placement.")]
        [Min(0f)] public float density = 2f;

        public PlacementCondition where = PlacementCondition.Range(0.1f, 1f, 0f, 30f);

        [Header("Per-instance variation")]
        [Tooltip("Uniform scale is randomized in this range.")]
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
        public bool randomYRotation = true;
        [Tooltip("Tilt the object to match the ground normal.")]
        public bool alignToNormal = false;
        [Tooltip("Push the object down into the ground so it doesn't float (world units).")]
        public float sink = 0f;
        [Tooltip("Scale each axis (X/Y/Z) independently within scaleRange for an uneven look. Used by rocks.")]
        public bool nonUniformScale = false;

        public bool IsValid => prefabs != null && prefabs.Length > 0 && count > 0;
    }

    /// <summary>
    /// A village: one or more zones where the heightmap is FLATTENED to a common level before buildings are
    /// placed on the level ground. Trees/rocks are kept out of these zones. Buildings come from Buildings/.
    /// </summary>
    [Serializable]
    public class VillageSettings
    {
        public bool enabled = false;
        [Tooltip("Building prefabs (from Buildings/); one chosen at random per building.")]
        public GameObject[] buildingPrefabs = Array.Empty<GameObject>();

        [Tooltip("How many village zones of this biome to place per island.")]
        [Min(0)] public int villageCount = 1;
        [Tooltip("World radius of the flattened build zone.")]
        public float villageRadius = 45f;
        [Tooltip("Extra width beyond the radius where the flattened ground blends back to the terrain.")]
        public float blendRadius = 18f;

        [Min(1)] public int buildingsPerVillage = 8;
        public float minDistanceBetweenVillages = 220f;
        public float minDistanceBetweenBuildings = 9f;

        [Tooltip("Preferred site for a village centre: height band (0..1) and max slope (deg). Flat low-mid " +
                 "ground is best; if nothing qualifies, the flattest candidate is used anyway.")]
        public PlacementCondition where = PlacementCondition.Range(0.12f, 0.7f, 0f, 14f);

        [Header("Buildings")]
        public Vector2 buildingScaleRange = new Vector2(1f, 1f);
        public bool randomYRotation = true;

        public bool IsValid => enabled && buildingPrefabs != null && buildingPrefabs.Length > 0 && villageCount > 0;
    }

    /// <summary>
    /// Per-biome grass: drives the procedural grass field (IslandSystem/Grass shader). Each biome can have its
    /// own colour, density, size and wind so the look varies per biome (e.g. lush green meadow vs sparse tundra).
    /// </summary>
    [Serializable]
    public class GrassSettings
    {
        public bool enabled = true;
        [Tooltip("Layer name, e.g. \"grass\", \"moss\", \"lichen\", \"flowers\".")]
        public string name = "grass";
        [Tooltip("Instances per 100 m² of eligible ground. Rendered GPU-instanced (RenderMeshInstanced), streamed near the camera.")]
        [Min(0f)] public float density = 200f;

        [Header("Prefab (the mesh that gets GPU-instanced)")]
        [Tooltip("Foliage PREFAB — grass blade/clump, moss patch, lichen, flower, etc. Its mesh is instanced; " +
                 "its material's texture is used with the wind/flatten grass shader.")]
        public GameObject prefab;
        [Tooltip("Uniform scale range per instance.")]
        public Vector2 prefabScaleRange = new Vector2(0.85f, 1.25f);
        [Tooltip("Lay the instance FLAT on the ground, oriented to the slope (for MOSS / LICHEN). Off = upright.")]
        public bool layFlat = false;
        [Tooltip("Alpha below this is cut away (for textured prefabs).")]
        [Range(0f, 1f)] public float alphaCutoff = 0.3f;

        [Header("Look (Cyanilux-style gradient)")]
        [Tooltip("Tint at the blade ROOT (multiplied over the texture). White = texture colour as-is (moss etc).")]
        public Color bottomColor = Color.white;
        [Tooltip("Tint at the blade TIP. Lighter than bottom gives the classic stylized-grass gradient.")]
        public Color topColor = Color.white;
        [Tooltip("The texture is an NxN atlas of blade variants; each instance picks a random tile. 1 = whole texture.")]
        [Min(1)] public int textureTiles = 1;

        [Header("Colour variation (world-noise patches: yellowed ↔ lush)")]
        [Tooltip("How strongly patches drift toward the DRY colours (0 = uniform colour everywhere).")]
        [Range(0f, 1f)] public float colorVariation = 0f;
        [Tooltip("Root tint inside the dry/yellowed patches.")]
        public Color dryBottomColor = Color.white;
        [Tooltip("Tip tint inside the dry/yellowed patches.")]
        public Color dryTopColor = Color.white;
        [Tooltip("Spatial frequency of the patches (1/metres): 0.03 ≈ ~30 m patches. Same noise field on every " +
                 "layer/biome, so the patches blend continuously across biome borders.")]
        public float variationScale = 0.03f;

        [Header("Wind")]
        [Range(0f, 1f)] public float windStrength = 0.12f;
        public float windSpeed = 1f;
        [Tooltip("Spatial frequency of the wind waves (lower = broader gusts).")]
        public float windScale = 0.15f;

        [Header("Interaction")]
        [Tooltip("How hard the grass flattens under players/interactors. (0 for flat moss.)")]
        [Range(0f, 2f)] public float bendStrength = 1f;

        [Tooltip("Where it grows: height band (0..1) + slope (deg). Default = low/flat ground.")]
        public PlacementCondition where = PlacementCondition.Range(0f, 1f, 0f, 28f);

        public bool IsValid => enabled && density > 0f && prefab != null;
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
        public ClimateZone climateZone = ClimateZone.Hot;
        [Tooltip("Which island type this zone belongs to (a biome is unique per island type).")]
        public IslandType islandType = IslandType.GrandCanyons;

        [Header("Zone character (as an island sub-biome)")]
        [Tooltip("Human-readable name, e.g. \"Пляж\". Falls back to biomeName if empty.")]
        public string displayName;
        [Tooltip("Where this zone sits on an island: 0 = shoreline, 1 = peaks. Orders the biome bands.")]
        [Range(0f, 1f)] public float elevationOrder = 0.5f;
        [Tooltip("Local ruggedness hint: plains low, hills/mountains high. (Reserved for relief shaping.)")]
        [Range(0f, 1f)] public float reliefStrength = 0.5f;
        [Tooltip("Preview colour used by the generator until real textures are scanned in.")]
        public Color debugColor = new Color(0.7f, 0.7f, 0.7f, 1f);

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
        [Tooltip("When a biome has several texture layers they're laid out as PATCHES (each covers ~its weight's " +
                 "share of the area). This is the patch density: higher = more, smaller patches. 0 = use default.")]
        public float texturePatchScale = 6f;
        public List<BiomeTextureLayer> textureLayers = new List<BiomeTextureLayer>();

        [Header("Objects — condition-driven spawn rules (from Props/)")]
        public List<ObjectSpawnRule> spawnRules = new List<ObjectSpawnRule>();

        [Header("Trees — Unity Terrain tree instances (from Trees/)")]
        public List<ObjectSpawnRule> treeRules = new List<ObjectSpawnRule>();

        [Header("Rocks — GameObjects with per-axis scale (from Rocks/)")]
        public List<ObjectSpawnRule> rockRules = new List<ObjectSpawnRule>();

        [Header("Village — flattened build zones (buildings from Buildings/)")]
        public VillageSettings village = new VillageSettings();

        [Header("Grass — procedural grass field (IslandSystem/Grass shader)")]
        [Tooltip("Grass layers for this biome — add several (e.g. a flat moss base + upright grass + flowers). " +
                 "Each layer is a prefab or a texture card, streamed only near the camera.")]
        public List<GrassSettings> grassLayers = new List<GrassSettings>();

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
