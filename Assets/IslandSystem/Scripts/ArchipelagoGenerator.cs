using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandSystem
{
    /// <summary>
    /// Spawns an archipelago in the scene: N islands scattered over a water field, each picking a biome
    /// from <see cref="biomes"/> and a random size, plus an ocean plane. Placement is radius-aware so
    /// islands of different sizes don't overlap. Built to run in the editor via the context menu /
    /// inspector button so the generated <see cref="TerrainData"/> is saved as a persistent asset.
    /// </summary>
    public class ArchipelagoGenerator : MonoBehaviour
    {
        [Header("Biomes")]
        [Tooltip("Palette: each island randomly picks one of these. If empty, falls back to 'biome' below.")]
        public List<BiomeAssetManifest> biomes = new List<BiomeAssetManifest>();
        [Tooltip("Fallback biome used only when the palette above is empty.")]
        public BiomeAssetManifest biome;

        [Header("Layout")]
        [Min(1)] public int islandCount = 5;
        public int baseSeed = 1000;
        [Tooltip("Islands are scattered within this radius around the origin.")]
        public float fieldRadius = 1100f;
        [Tooltip("Minimum open-water gap between island land edges (units).")]
        public float minGap = 130f;

        [Header("Size variation")]
        [Tooltip("Per-island multiplier applied to the biome's terrainSize (x/z and height).")]
        public Vector2 sizeMultiplierRange = new Vector2(0.65f, 1.5f);
        [Tooltip("Fraction of the terrain tile that is actually land, used for spacing (~0.6 for falloff ~2).")]
        [Range(0.3f, 1f)] public float landFraction = 0.62f;

        [Header("Ocean")]
        public Material waterMaterial;
        [Tooltip("World Y of the sea surface. Islands sit on Y=0, so a small positive value submerges the flat skirt.")]
        public float waterLevel = 4f;
        [Tooltip("Side length of the ocean plane (units). A Unity plane is 10u, so scale = size / 10.")]
        public float oceanSize = 4500f;

        [Header("Output")]
        [Tooltip("Folder where generated TerrainData assets are written (must exist).")]
        public string generatedFolder = "Assets/IslandSystem/Generated";

        const string RootName = "Archipelago";

        struct Placed { public Vector3 center; public float landRadius; }

        [ContextMenu("Generate")]
        public void Generate()
        {
            var palette = BuildPalette();
            if (palette.Count == 0)
            {
                Debug.LogError("[Archipelago] No biomes assigned (palette and fallback are both empty).", this);
                return;
            }

            Clear();
            CleanGeneratedTerrain();

            var root = new GameObject(RootName).transform;
            root.SetParent(transform, false);

            Material terrainMat = GetOrCreateTerrainMaterial();
            var placed = new List<Placed>(islandCount);

            for (int i = 0; i < islandCount; i++)
            {
                int seed = baseSeed + i * 977;
                var rng = new System.Random(seed);

                BiomeAssetManifest b = palette[rng.Next(palette.Count)];
                float mult = Mathf.Lerp(sizeMultiplierRange.x, sizeMultiplierRange.y, (float)rng.NextDouble());
                Vector3 size = Vector3.Scale(b.terrainSize, new Vector3(mult, mult, mult));

                float landRadius = Mathf.Max(size.x, size.z) * 0.5f * landFraction;
                Vector3 center = FindPlacement(rng, placed, landRadius);
                placed.Add(new Placed { center = center, landRadius = landRadius });

                TerrainData data = IslandTerrainGenerator.BuildTerrainData(b, seed, size);
                data.name = $"Island_{i}_TerrainData";
#if UNITY_EDITOR
                string path = $"{generatedFolder}/Island_{i}.asset";
                AssetDatabase.CreateAsset(data, path);
#endif

                GameObject terrainGO = Terrain.CreateTerrainGameObject(data);
                terrainGO.name = $"Island_{i}_{b.biomeName}";
                terrainGO.transform.SetParent(root, true);
                // Center the island tile on its placement point.
                terrainGO.transform.position = center + new Vector3(-size.x * 0.5f, 0f, -size.z * 0.5f);

                var terrain = terrainGO.GetComponent<Terrain>();
                if (terrainMat != null) terrain.materialTemplate = terrainMat;

                IslandTerrainGenerator.ScatterObjects(terrain, b, seed);
            }

            CreateOcean(root);

#if UNITY_EDITOR
            AssetDatabase.SaveAssets();
#endif
            Debug.Log($"[Archipelago] Generated {islandCount} island(s) from {palette.Count} biome(s).", this);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == RootName)
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }
        }

        // ---- Biome palette -----------------------------------------------

        List<BiomeAssetManifest> BuildPalette()
        {
            var palette = new List<BiomeAssetManifest>();
            if (biomes != null)
                foreach (var b in biomes)
                    if (b != null) palette.Add(b);
            if (palette.Count == 0 && biome != null) palette.Add(biome);
            return palette;
        }

        // ---- Placement ----------------------------------------------------

        /// <summary>
        /// Rejection-samples a point inside the field disk that keeps <paramref name="landRadius"/> clear
        /// of every already-placed island (plus <see cref="minGap"/>). If it can't fit after many tries,
        /// it pushes the island out past the field so it never overlaps.
        /// </summary>
        Vector3 FindPlacement(System.Random rng, List<Placed> placed, float landRadius)
        {
            const int attempts = 256;
            for (int a = 0; a < attempts; a++)
            {
                float r = fieldRadius * Mathf.Sqrt((float)rng.NextDouble());
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);

                bool ok = true;
                foreach (var p in placed)
                {
                    if (Vector3.Distance(c, p.center) < landRadius + p.landRadius + minGap) { ok = false; break; }
                }
                if (ok) return c;
            }

            // Fallback: place it on a ring beyond everything currently placed.
            float maxReach = fieldRadius;
            foreach (var p in placed)
                maxReach = Mathf.Max(maxReach, new Vector2(p.center.x, p.center.z).magnitude + p.landRadius);
            float outAng = (float)rng.NextDouble() * Mathf.PI * 2f;
            float outR = maxReach + landRadius + minGap;
            return new Vector3(Mathf.Cos(outAng) * outR, 0f, Mathf.Sin(outAng) * outR);
        }

        // ---- Ocean & assets ----------------------------------------------

        void CreateOcean(Transform root)
        {
            var ocean = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ocean.name = "Ocean";
            ocean.transform.SetParent(root, true);
            ocean.transform.position = new Vector3(0f, waterLevel, 0f);
            ocean.transform.localScale = new Vector3(oceanSize / 10f, 1f, oceanSize / 10f);

            var renderer = ocean.GetComponent<Renderer>();
            if (waterMaterial != null)
            {
                renderer.sharedMaterial = waterMaterial;
            }
            else
            {
                // TODO: assign a real water material (UberStylizedWater). Fallback URP-lit blue stub for now.
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var stub = new Material(shader) { name = "OceanStub" };
                stub.color = new Color(0.15f, 0.45f, 0.75f, 1f);
                renderer.sharedMaterial = stub;
                Debug.LogWarning("[Archipelago] No water material assigned; using a blue URP-lit stub.", this);
            }
        }

        void CleanGeneratedTerrain()
        {
#if UNITY_EDITOR
            // Delete previously generated TerrainData so changing islandCount/biomes never leaks assets.
            foreach (var guid in AssetDatabase.FindAssets("t:TerrainData", new[] { generatedFolder }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
#endif
        }

        Material GetOrCreateTerrainMaterial()
        {
            // URP needs an explicit terrain material or the terrain renders grey/magenta.
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null) return null; // Built-in pipeline supplies its own default.

#if UNITY_EDITOR
            string matPath = $"{generatedFolder}/URP_Terrain.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null) return existing;
            var mat = new Material(shader) { name = "URP_Terrain" };
            AssetDatabase.CreateAsset(mat, matPath);
            return mat;
#else
            return new Material(shader) { name = "URP_Terrain" };
#endif
        }
    }
}
