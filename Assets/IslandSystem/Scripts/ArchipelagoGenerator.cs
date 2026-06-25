using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandSystem
{
    /// <summary>
    /// Spawns a small archipelago in the scene: N islands of one biome (different seeds) laid out in a
    /// ring, plus an ocean plane. Built to run in the editor via the context menu / inspector button so
    /// the generated <see cref="TerrainData"/> is saved as a persistent asset.
    /// </summary>
    public class ArchipelagoGenerator : MonoBehaviour
    {
        [Header("Biome")]
        public BiomeAssetManifest biome;

        [Header("Layout")]
        [Min(1)] public int islandCount = 3;
        public int baseSeed = 1000;
        [Tooltip("Distance between island centers (units).")]
        public float spacing = 400f;

        [Header("Ocean")]
        public Material waterMaterial;
        [Tooltip("World Y of the sea surface. Islands sit on Y=0, so a small negative value reads as a beach.")]
        public float waterLevel = -1f;
        [Tooltip("Side length of the ocean plane (units). A Unity plane is 10u, so scale = size / 10.")]
        public float oceanSize = 4000f;

        [Header("Output")]
        [Tooltip("Folder where generated TerrainData assets are written (must exist).")]
        public string generatedFolder = "Assets/IslandSystem/Generated";

        const string RootName = "Archipelago";

        [ContextMenu("Generate")]
        public void Generate()
        {
            if (biome == null)
            {
                Debug.LogError("[Archipelago] No biome assigned.", this);
                return;
            }

            Clear();

            var root = new GameObject(RootName).transform;
            root.SetParent(transform, false);

            Vector3 size = biome.terrainSize;
            // Center each island on its layout point by offsetting the terrain corner.
            Vector3 cornerOffset = new Vector3(-size.x * 0.5f, 0f, -size.z * 0.5f);

            Material terrainMat = GetOrCreateTerrainMaterial();

            for (int i = 0; i < islandCount; i++)
            {
                Vector3 center = LayoutPoint(i, islandCount, spacing);
                int seed = baseSeed + i * 977;

                TerrainData data = IslandTerrainGenerator.BuildTerrainData(biome, seed);
                data.name = $"Island_{i}_TerrainData";
#if UNITY_EDITOR
                // Deterministic path + overwrite so regenerating never leaks duplicate assets.
                string path = $"{generatedFolder}/{biome.biomeName}_Island_{i}.asset";
                if (AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null)
                    AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(data, path);
#endif

                GameObject terrainGO = Terrain.CreateTerrainGameObject(data);
                terrainGO.name = $"Island_{i}";
                terrainGO.transform.SetParent(root, true);
                terrainGO.transform.position = center + cornerOffset;

                var terrain = terrainGO.GetComponent<Terrain>();
                if (terrainMat != null) terrain.materialTemplate = terrainMat;

                IslandTerrainGenerator.ScatterProps(terrain, biome, seed);
            }

            CreateOcean(root);

#if UNITY_EDITOR
            AssetDatabase.SaveAssets();
#endif
            Debug.Log($"[Archipelago] Generated {islandCount} island(s) of biome '{biome.biomeName}'.", this);
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

        /// <summary>Positions on a circle (single island -> origin) so islands sit in a clean ring.</summary>
        static Vector3 LayoutPoint(int index, int count, float spacing)
        {
            if (count <= 1) return Vector3.zero;
            // Radius so neighbour-to-neighbour chord ~= spacing.
            float radius = spacing / (2f * Mathf.Sin(Mathf.PI / count));
            float ang = (Mathf.PI * 2f / count) * index;
            return new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
        }

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
