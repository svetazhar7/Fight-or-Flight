using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandSystem
{
    /// <summary>
    /// Spawns an archipelago: <c>clamp(level,1,3)</c> HUB islands spread far apart on a ring, and around
    /// each hub a cluster of satellite islands (configurable counts of large / medium / small), plus an
    /// ocean + seabed sized to cover everything. Placement is radius-aware so islands never overlap. Runs
    /// in the editor via the context menu / inspector button so each <see cref="TerrainData"/> is saved.
    /// </summary>
    public class ArchipelagoGenerator : MonoBehaviour
    {
        [Header("Island types")]
        [Tooltip("Palette of island TYPE definitions. Each island picks one and is composed of its weighted " +
                 "biomes (painted into elevation bands by %).")]
        public List<IslandTypeDefinition> islandTypes = new List<IslandTypeDefinition>();

        [Header("Level & hubs")]
        [Min(1)]
        [Tooltip("Archipelago level. Hub count = clamp(level, 1, 3): L1 → 1 hub, L2 → 2, L3 and above → 3.")]
        public int level = 1;
        [Tooltip("Optional palette for HUB islands (the main, large islands). Falls back to 'Island types' if empty.")]
        public List<IslandTypeDefinition> hubIslandTypes = new List<IslandTypeDefinition>();
        [Tooltip("Size multiplier range for hubs (the big main islands).")]
        public Vector2 hubSizeMultiplierRange = new Vector2(1.7f, 2.1f);
        [Tooltip("Distance between hubs — they are spread far apart on a ring.")]
        public float hubSeparation = 2600f;

        /// <summary>Hubs never exceed this many, however high the level grows.</summary>
        public const int MaxHubs = 3;

        [Header("Satellites around each hub")]
        [Min(0)] public int largePerHub = 2;
        [Min(0)] public int mediumPerHub = 3;
        [Min(0)] public int smallPerHub = 4;
        public Vector2 largeSizeRange = new Vector2(1.0f, 1.3f);
        public Vector2 mediumSizeRange = new Vector2(0.6f, 0.85f);
        public Vector2 smallSizeRange = new Vector2(0.32f, 0.5f);
        [Tooltip("Radius around a hub within which its satellite islands are scattered.")]
        public float clusterRadius = 950f;

        [Header("Placement")]
        public int baseSeed = 1000;
        [Tooltip("Minimum open-water gap between island land edges (units).")]
        public float minGap = 130f;
        [Range(0.3f, 1f)]
        [Tooltip("Fraction of the terrain tile that is actually land, used for spacing (~0.6 for falloff ~2).")]
        public float landFraction = 0.62f;

        [Header("Ocean")]
        public Material waterMaterial;
        [Tooltip("World Y of the sea surface. Islands sit on Y=0.")]
        public float waterLevel = 4f;
        [Tooltip("Minimum ocean size; auto-expands to cover the whole archipelago.")]
        public float oceanSize = 4500f;

        [Header("Seabed")]
        [Tooltip("Adds a floor under the water so open ocean isn't a void (and the toon water has depth to read).")]
        public bool createSeabed = true;
        public Material seabedMaterial;
        [Tooltip("World Y of the seabed plane. Below the islands' Y=0 base and the water surface.")]
        public float seabedLevel = -1f;

        [Header("Output")]
        [Tooltip("Folder where generated TerrainData assets are written (must exist).")]
        public string generatedFolder = "Assets/IslandSystem/Generated";

        const string RootName = "Archipelago";

        struct Placed { public Vector3 center; public float landRadius; }

        [ContextMenu("Generate")]
        public void Generate()
        {
            var typePalette = BuildTypePalette();
            if (typePalette.Count == 0)
            {
                Debug.LogError("[Archipelago] No island types assigned.", this);
                return;
            }

            Clear();
            CleanGeneratedTerrain();

            var root = new GameObject(RootName).transform;
            root.SetParent(transform, false);

            Material terrainMat = GetOrCreateTerrainMaterial();

            int hubCount = Mathf.Clamp(level, 1, MaxHubs);          // L1→1, L2→2, L3+→3
            var hubPalette = BuildHubPalette(typePalette);
            Vector3[] hubCenters = HubCenters(hubCount, hubSeparation);
            var placed = new List<Placed>();
            int idx = 0;

            // 1) Hubs at their ring positions (far apart).
            for (int h = 0; h < hubCount; h++)
            {
                int seed = baseSeed + idx * 977;
                var rng = new System.Random(seed);
                var def = hubPalette[rng.Next(hubPalette.Count)];
                Vector3 size = SizeFor(def, hubSizeMultiplierRange, rng);
                float landRadius = LandRadius(size);
                placed.Add(new Placed { center = hubCenters[h], landRadius = landRadius });
                SpawnIsland(root, terrainMat, def, seed, size, hubCenters[h], true, "Hub", h, ResForTier(def, 2));
                idx++;
            }

            // 2) Satellite cluster around each hub: configurable large / medium / small counts.
            var cats = new (int count, Vector2 range, int tier)[]
            {
                (largePerHub,  largeSizeRange,  2),
                (mediumPerHub, mediumSizeRange, 1),
                (smallPerHub,  smallSizeRange,  0),
            };
            for (int h = 0; h < hubCount; h++)
            {
                foreach (var cat in cats)
                {
                    for (int c = 0; c < cat.count; c++)
                    {
                        int seed = baseSeed + idx * 977;
                        var rng = new System.Random(seed);
                        var def = typePalette[rng.Next(typePalette.Count)];
                        Vector3 size = SizeFor(def, cat.range, rng);
                        float landRadius = LandRadius(size);
                        Vector3 center = FindPlacementNear(rng, hubCenters[h], clusterRadius, placed, landRadius);
                        placed.Add(new Placed { center = center, landRadius = landRadius });
                        SpawnIsland(root, terrainMat, def, seed, size, center, false, "Island", idx, ResForTier(def, cat.tier));
                        idx++;
                    }
                }
            }

            // Ocean + seabed sized to cover the whole (now much larger) archipelago.
            float maxReach = oceanSize * 0.5f;
            foreach (var p in placed)
                maxReach = Mathf.Max(maxReach, new Vector2(p.center.x, p.center.z).magnitude + p.landRadius);
            float fieldSize = Mathf.Max(oceanSize, (maxReach + 800f) * 2f);

            if (createSeabed) CreateSeabed(root, fieldSize);
            CreateOcean(root, fieldSize);

#if UNITY_EDITOR
            AssetDatabase.SaveAssets();
#endif
            Debug.Log($"[Archipelago] Level {level}: {hubCount} hub(s), each + {largePerHub}L/{mediumPerHub}M/{smallPerHub}S " +
                      $"satellites = {idx} islands total.", this);
        }

        // ---- Building one island -----------------------------------------

        Vector3 SizeFor(IslandTypeDefinition def, Vector2 range, System.Random rng)
        {
            float mult = Mathf.Lerp(range.x, range.y, (float)rng.NextDouble());
            return Vector3.Scale(def.terrainSize, new Vector3(mult, mult, mult));
        }

        float LandRadius(Vector3 size) => Mathf.Max(size.x, size.z) * 0.5f * landFraction;

        /// <summary>Heightmap resolution by size tier (0=small,1=medium,2=large/hub) — cheaper small islands.</summary>
        int ResForTier(IslandTypeDefinition def, int tier)
        {
            int baseRes = Mathf.Max(33, def.heightmapResolution);
            if (tier == 0) return Mathf.Min(baseRes, 129);
            if (tier == 1) return Mathf.Min(baseRes, 257);
            return baseRes;
        }

        void SpawnIsland(Transform root, Material terrainMat, IslandTypeDefinition def, int seed,
            Vector3 size, Vector3 center, bool isHub, string prefix, int nameIndex, int resolution)
        {
            // Create the TerrainData asset FIRST, then populate it (SetAlphamaps only persists on a live asset).
            var data = new TerrainData { name = $"{prefix}_{nameIndex}_TerrainData" };
#if UNITY_EDITOR
            AssetDatabase.CreateAsset(data, $"{generatedFolder}/{prefix}_{nameIndex}.asset");
#endif
            float waterline = waterLevel / Mathf.Max(0.0001f, size.y);
            IslandTerrainGenerator.PopulateIslandFromType(data, def, seed, size, waterline, out var bands, resolution);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = $"{prefix}_{nameIndex}_{def.islandType}";
            go.transform.SetParent(root, true);
            go.transform.position = center + new Vector3(-size.x * 0.5f, 0f, -size.z * 0.5f);

            var terrain = go.GetComponent<Terrain>();
            if (terrainMat != null) terrain.materialTemplate = terrainMat;

            IslandTerrainGenerator.ScatterCompositionObjects(terrain, def, seed, bands);

            var marker = go.AddComponent<IslandMarker>();
            marker.isHub = isHub;
            marker.climateZone = def.climateZone;
            marker.islandType = def.islandType;
            marker.level = level;
        }

        // ---- Palettes -----------------------------------------------------

        List<IslandTypeDefinition> BuildTypePalette()
        {
            var palette = new List<IslandTypeDefinition>();
            if (islandTypes != null)
                foreach (var t in islandTypes)
                    if (t != null) palette.Add(t);
            return palette;
        }

        /// <summary>Hub palette: explicit hub types if any, otherwise the main island-type palette.</summary>
        List<IslandTypeDefinition> BuildHubPalette(List<IslandTypeDefinition> fallback)
        {
            var palette = new List<IslandTypeDefinition>();
            if (hubIslandTypes != null)
                foreach (var t in hubIslandTypes)
                    if (t != null) palette.Add(t);
            return palette.Count > 0 ? palette : fallback;
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

        // ---- Placement ----------------------------------------------------

        /// <summary>Hub centers evenly spaced on a ring so neighbours are ~<paramref name="sep"/> apart.</summary>
        static Vector3[] HubCenters(int count, float sep)
        {
            var arr = new Vector3[Mathf.Max(1, count)];
            if (count <= 1) { arr[0] = Vector3.zero; return arr; }
            float radius = sep / (2f * Mathf.Sin(Mathf.PI / count));
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI * 2f * i / count;
                arr[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            return arr;
        }

        /// <summary>
        /// Rejection-samples a spot inside the cluster disk around <paramref name="hub"/> that keeps
        /// <paramref name="landRadius"/> clear of every placed island (+ <see cref="minGap"/>). Falls back
        /// to expanding rings around the hub if the disk is full.
        /// </summary>
        Vector3 FindPlacementNear(System.Random rng, Vector3 hub, float radius, List<Placed> placed, float landRadius)
        {
            const int attempts = 300;
            for (int a = 0; a < attempts; a++)
            {
                float r = radius * Mathf.Sqrt((float)rng.NextDouble());
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 c = hub + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                if (IsClear(c, placed, landRadius)) return c;
            }
            for (float rr = radius; rr < radius * 3f; rr += landRadius + minGap)
            {
                for (int s = 0; s < 16; s++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    Vector3 c = hub + new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr);
                    if (IsClear(c, placed, landRadius)) return c;
                }
            }
            return hub + new Vector3(radius * 1.5f, 0f, 0f);
        }

        bool IsClear(Vector3 c, List<Placed> placed, float landRadius)
        {
            foreach (var p in placed)
                if (Vector3.Distance(c, p.center) < landRadius + p.landRadius + minGap) return false;
            return true;
        }

        // ---- Ocean & assets ----------------------------------------------

        void CreateSeabed(Transform root, float size)
        {
            var bed = GameObject.CreatePrimitive(PrimitiveType.Plane);
            bed.name = "Seabed";
            bed.transform.SetParent(root, true);
            bed.transform.position = new Vector3(0f, seabedLevel, 0f);
            bed.transform.localScale = new Vector3(size / 10f * 1.1f, 1f, size / 10f * 1.1f);

            var renderer = bed.GetComponent<Renderer>();
            if (seabedMaterial != null)
            {
                renderer.sharedMaterial = seabedMaterial;
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                renderer.sharedMaterial = new Material(shader) { name = "SeabedStub", color = new Color(0.40f, 0.36f, 0.27f, 1f) };
            }
        }

        void CreateOcean(Transform root, float size)
        {
            var ocean = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ocean.name = "Ocean";
            ocean.transform.SetParent(root, true);
            ocean.transform.position = new Vector3(0f, waterLevel, 0f);
            ocean.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);

            var renderer = ocean.GetComponent<Renderer>();
            if (waterMaterial != null)
            {
                renderer.sharedMaterial = waterMaterial;
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                renderer.sharedMaterial = new Material(shader) { name = "OceanStub", color = new Color(0.15f, 0.45f, 0.75f, 1f) };
                Debug.LogWarning("[Archipelago] No water material assigned; using a blue URP-lit stub.", this);
            }
        }

        void CleanGeneratedTerrain()
        {
#if UNITY_EDITOR
            // Delete previously generated TerrainData so changing level/counts never leaks assets.
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
