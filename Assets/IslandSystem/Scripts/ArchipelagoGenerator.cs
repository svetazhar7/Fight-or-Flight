using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandSystem
{
    /// <summary>
    /// Spawns an archipelago: ONE central HUB island at the origin, surrounded by a cluster of satellite
    /// islands (configurable counts of large / medium / small), plus an ocean + seabed sized to cover
    /// everything. The hub and each satellite size range are configurable (multipliers of the island type's
    /// terrainSize). Placement is radius-aware so islands never overlap. Runs in the editor via the context
    /// menu / inspector button so each <see cref="TerrainData"/> is saved.
    /// </summary>
    public class ArchipelagoGenerator : MonoBehaviour
    {
        [Header("Island types")]
        [Tooltip("Palette of island TYPE definitions. Each island picks one and is composed of its weighted " +
                 "biomes (painted into elevation bands by %).")]
        public List<IslandTypeDefinition> islandTypes = new List<IslandTypeDefinition>();

        [Header("Central hub")]
        [Tooltip("Optional palette for the HUB island (the main, large central island). Falls back to 'Island types' if empty.")]
        public List<IslandTypeDefinition> hubIslandTypes = new List<IslandTypeDefinition>();
        [Tooltip("Size multiplier range for the central hub island (multiplies its type's terrainSize) — set it " +
                 "just like the satellite island size ranges below.")]
        public Vector2 hubSizeMultiplierRange = new Vector2(1.7f, 2.1f);

        [Header("Satellite islands (around the hub)")]
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
        [Tooltip("Water material for the inland LAKES (a tinted, pond-like variant). Falls back to the ocean material if null.")]
        public Material lakeMaterial;
        [Tooltip("Minimum ocean size; auto-expands to cover the whole archipelago.")]
        public float oceanSize = 4500f;

        [Header("Seabed")]
        [Tooltip("Adds a floor under the water so open ocean isn't a void (and the toon water has depth to read).")]
        public bool createSeabed = true;
        public Material seabedMaterial;
        [Tooltip("World Y of the single seabed plane. Each island tile is sunk so its flat skirt sits just " +
                 "below this plane — the seabed hides the overlapping tile floors, so islands rise out of one " +
                 "continuous seabed with no Z-fighting and no square tile seams. Keep it below waterLevel.")]
        public float seabedLevel = -1f;

        /// <summary>How far below the seabed plane each island tile's flat base is sunk so the seabed hides it.</summary>
        const float IslandFloorSink = 1f;

        [Header("Output")]
        [Tooltip("Folder where generated TerrainData assets are written (must exist).")]
        public string generatedFolder = "Assets/IslandSystem/Generated";

        const string RootName = "Archipelago";

        struct Placed { public Vector3 center; public float landRadius; }

        [ContextMenu("Generate")]
        public void Generate()
        {
            // Explicit editor (re)generate: drop the memoized islands FIRST so edits to a biome / island
            // definition actually take effect. The island cache keys on the definition's INSTANCE id + seed,
            // not its CONTENT — so without this, a cached island replays the stale heightmap & splatmaps and
            // pressing "Generate" looks like it does nothing after you change settings. (Runtime generation via
            // GenerateAtRuntime keeps the cache: at runtime the definition content is fixed and the varying seed
            // is already part of the key, so cached islands stay valid and re-hosting the same world is fast.)
            IslandTerrainGenerator.ClearIslandCache();
            GenerateInternal();
        }

        void GenerateInternal()
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

            var hubPalette = BuildHubPalette(typePalette);
            var placed = new List<Placed>();
            int idx = 0;

            // 1) The single central HUB island, at the origin.
            Vector3 hubCenter = Vector3.zero;
            {
                int seed = baseSeed + idx * 977;
                var rng = new System.Random(seed);
                var def = hubPalette[rng.Next(hubPalette.Count)];
                Vector3 size = SizeFor(def, hubSizeMultiplierRange, rng);
                placed.Add(new Placed { center = hubCenter, landRadius = LandRadius(size) });
                SpawnIsland(root, terrainMat, def, seed, size, hubCenter, true, "Hub", 0, ResForTier(def, 2));
                idx++;
            }

            // 2) Satellite islands scattered around the central hub: configurable large / medium / small counts.
            var cats = new (int count, Vector2 range, int tier)[]
            {
                (largePerHub,  largeSizeRange,  2),
                (mediumPerHub, mediumSizeRange, 1),
                (smallPerHub,  smallSizeRange,  0),
            };
            foreach (var cat in cats)
            {
                for (int c = 0; c < cat.count; c++)
                {
                    int seed = baseSeed + idx * 977;
                    var rng = new System.Random(seed);
                    var def = typePalette[rng.Next(typePalette.Count)];
                    Vector3 size = SizeFor(def, cat.range, rng);
                    float landRadius = LandRadius(size);
                    Vector3 center = FindPlacementNear(rng, hubCenter, clusterRadius, placed, landRadius);
                    placed.Add(new Placed { center = center, landRadius = landRadius });
                    SpawnIsland(root, terrainMat, def, seed, size, center, false, "Island", idx, ResForTier(def, cat.tier));
                    idx++;
                }
            }

            // Ocean + seabed sized to cover the whole (now much larger) archipelago.
            float maxReach = oceanSize * 0.5f;
            foreach (var p in placed)
                maxReach = Mathf.Max(maxReach, new Vector2(p.center.x, p.center.z).magnitude + p.landRadius);
            float fieldSize = Mathf.Max(oceanSize, (maxReach + 800f) * 2f);

            if (createSeabed) CreateSeabed(root, fieldSize);
            CreateOcean(root, fieldSize);

            // Storm wall around the world edge — radius adapts to this map's ocean size each generation.
            // Create it if missing so the wall is self-healing across regenerations.
            var stormWall = FindAnyObjectByType<StormWall>();
            if (stormWall == null)
                stormWall = new GameObject("Storm Wall").AddComponent<StormWall>();
            stormWall.Rebuild(fieldSize * 0.5f);
            // Lightning that strikes inside the storm-cloud ring (COZY thunder, play mode).
            var stormLightning = stormWall.GetComponent<StormLightning>();
            if (stormLightning == null)
                stormLightning = stormWall.gameObject.AddComponent<StormLightning>();
#if UNITY_EDITOR
            if (stormLightning.lightningPrefab == null)
                stormLightning.lightningPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Packages/com.distantlands.cozy.core/Content/Prefabs/Thunder And Lightning.prefab");
#endif

#if UNITY_EDITOR
            if (!Application.isPlaying) AssetDatabase.SaveAssets();
#endif
            Debug.Log($"[Archipelago] 1 central hub + {largePerHub}L/{mediumPerHub}M/{smallPerHub}S " +
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
            // At edit time the asset must exist BEFORE SetAlphamaps (the CreateAsset-drops-splat quirk). At
            // runtime there's no AssetDatabase, so the TerrainData stays in-memory and SetAlphamaps persists.
            if (!Application.isPlaying)
                AssetDatabase.CreateAsset(data, $"{generatedFolder}/{prefix}_{nameIndex}.asset");
#endif
            // Sink each tile so its dead-flat skirt (terrain base, height 0) sits BELOW the single seabed
            // plane, which then hides it. Islands appear to rise out of one continuous seabed — no overlapping
            // flat tile floors fighting each other (Z-fighting) and no square tile seams. The waterline is
            // re-expressed relative to this lowered base so the biome bands still line up with the sea surface.
            float baseY = seabedLevel - IslandFloorSink;
            float waterline = (waterLevel - baseY) / Mathf.Max(0.0001f, size.y);
            IslandTerrainGenerator.PopulateIslandFromType(data, def, seed, size, waterline, out var bands, out var villages, out var lakes, resolution);

            // Scatter/stream keep-out zones = villages + lakes (so trees/rocks/grass/flowers never spawn in the
            // water). Village BUILDINGS still use the real village list (below), not this combined one.
            var keepOut = new List<IslandTerrainGenerator.VillageZone>(villages);
            foreach (var lk in lakes)
                keepOut.Add(new IslandTerrainGenerator.VillageZone { centerUV = lk.centerUV, radiusWorld = lk.radiusWorld + 3f });

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = $"{prefix}_{nameIndex}_{def.islandType}";
            go.transform.SetParent(root, true);
            go.transform.position = center + new Vector3(-size.x * 0.5f, baseY, -size.z * 0.5f);

            var terrain = go.GetComponent<Terrain>();
            terrain.allowAutoConnect = false; // islands are independent; avoids "different heightmap resolution" spam
            if (terrainMat != null) terrain.materialTemplate = terrainMat;

            // ---- terrain rendering optimization ----
            // Unity Terrain is already internally chunked (a quadtree of patches with continuous LOD); we tune
            // that built-in system rather than split islands into separate Terrains (which would add seams +
            // draw calls). GPU-instance the patches, drop the terrain's own tree/detail pass (we use our own
            // instanced trees/grass), and let TerrainLOD ramp pixelError + basemap by distance to the viewer.
            terrain.drawInstanced = true;                 // one instanced draw for the patch grid
            terrain.drawTreesAndFoliage = false;          // no Unity terrain trees/details in this project
            terrain.heightmapPixelError = 5f;             // base (near) LOD; TerrainLOD raises it with distance
            terrain.basemapDistance = 5000f;              // placeholder — TerrainLOD sets a whole-island value
            go.AddComponent<TerrainLOD>();                // per-island geometry LOD + whole-island basemap

            // Clearings (meadows): tree-free openings. They gate ONLY the trees — rocks/flowers/grass keep their
            // normal keep-out (villages + lakes), so the openings fill with ground cover.
            var clearings = IslandTerrainGenerator.PlaceClearings(terrain, bands, seed);
            var treeKeepOut = new List<IslandTerrainGenerator.VillageZone>(keepOut);
            treeKeepOut.AddRange(clearings);

            IslandTerrainGenerator.ScatterCompositionObjects(terrain, def, seed, bands, keepOut);
            IslandTerrainGenerator.PlaceTrees(terrain, def, seed, bands, treeKeepOut);   // trees avoid villages + lakes + CLEARINGS
            IslandTerrainGenerator.ScatterRocks(terrain, def, seed, bands, keepOut); // rocks (fill clearings too)
            IslandTerrainGenerator.PlaceVillageBuildings(terrain, villages, seed);   // buildings on the flattened ground (real villages)

            // Lake water surfaces — a flat disc at each lake's (possibly elevated) water level, reusing the ocean water.
            foreach (var lk in lakes) CreateLakeWater(go.transform, lk, size);

            var marker = go.AddComponent<IslandMarker>();
            marker.isHub = isHub;
            marker.climateZone = def.climateZone;
            marker.islandType = def.islandType;
            foreach (var b in bands) marker.bands.Add(new IslandBand { biome = b.biome, lo = b.lo, hi = b.hi });
            marker.villages.AddRange(keepOut);   // streamed systems (flowers/grass) keep out of villages AND lakes

            // Procedural grass: attach a streamer that builds grass in CHUNKS around the camera (only if any
            // biome has grass layers). Nothing is built until the camera is near — cheap on huge islands.
            if (GrassGenerator.HasAnyGrass(marker.bands))
            {
                var field = go.AddComponent<IslandGrassField>();
                field.terrain = terrain;
                field.seed = seed;
                field.waterline = waterline;
            }

            // Flowers stream like the grass: chunked around the viewer, built only where the camera looks
            // (frustum-gated). Nothing is spawned at generation time.
            if (IslandFlowerField.HasAnyFlowers(marker.bands))
            {
                var flowers = go.AddComponent<IslandFlowerField>();
                flowers.terrain = terrain;
                flowers.seed = seed;
            }
        }

        // ---- Palettes -----------------------------------------------------

        /// <summary>
        /// Runtime / network entry point: apply the (server-chosen, synced) seed and generate the archipelago
        /// LOCALLY with in-memory terrains (no AssetDatabase). Every peer that calls this with the same seed
        /// builds an identical world — that's how multiplayer terrain stays in sync.
        /// </summary>
        public void GenerateAtRuntime(int seed)
        {
            baseSeed = seed;
            GenerateInternal();   // keep the island cache at runtime (def content is fixed; seed is in the key)
        }

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
            // Span well beyond the ocean hexagon (radius = size) so there's always a floor under the water.
            bed.transform.localScale = new Vector3(size / 10f * 2.4f, 1f, size / 10f * 2.4f);

            var renderer = bed.GetComponent<Renderer>();
            if (seabedMaterial != null)
            {
                renderer.sharedMaterial = seabedMaterial;
                // The plane's UVs span 0..1 across the WHOLE seabed, and the seabed size adapts to the map — so
                // tile the texture per-instance (world repeat every ~SeabedTextureTile m) via a property block.
                float worldSize = size / 10f * 2.4f * 10f;
                float reps = Mathf.Max(1f, worldSize / SeabedTextureTile);
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector("_BaseMap_ST", new Vector4(reps, reps, 0f, 0f));
                renderer.SetPropertyBlock(mpb);
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                // Bright SAND — the seabed reads through the water and drives the shallow turquoise look;
                // the old muddy olive (0.40,0.36,0.27) made the whole sea murky-dark.
                renderer.sharedMaterial = new Material(shader) { name = "SeabedStub", color = new Color(0.76f, 0.70f, 0.52f, 1f) };
            }
        }

        /// <summary>World-space repeat (m) of the seabed texture.</summary>
        const float SeabedTextureTile = 4f;

        void CreateOcean(Transform root, float size)
        {
            // NATIVE Poseidon 2 water, built like the "Demo_LowPolyWater_Tileable" reference:
            //  - "Water Detail": a viewer-following block of finely tessellated hexagon tiles — the Gerstner
            //    WAVES and the low-poly facets actually show near the player;
            //  - "Water Horizon": one huge low-res tile so the open sea reaches past the storm ring;
            //  - PlanarReflectionRenderer mirrors the sky + SUN + clouds into the shared material each frame;
            //  - OceanTide slowly raises/lowers the whole ocean (приливы/отливы) on top of the fast waves.
            // Tiles auto-go on the "Water" layer (excluded from the reflection camera).
            var ocean = new GameObject("Ocean");
            ocean.transform.SetParent(root, true);
            ocean.transform.position = new Vector3(0f, waterLevel, 0f);

            // Thin huge collider at the surface for gameplay queries (splash/landing checks).
            var box = ocean.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.05f, 0f);
            box.size = new Vector3(size * 2.4f, 0.1f, size * 2.4f);

            if (waterMaterial == null)
            {
                Debug.LogWarning("[Archipelago] No water material assigned — ocean water skipped.", this);
                return;
            }

            // -- OCEAN LOD: two viewer-following rings + a horizon sheet -------------------------------
            // The old detail block was 6×6 uniform res-100 tiles (~2.2M verts) everywhere near the viewer.
            // Now the tessellation drops with distance: a small FINE ring right around the player, a big COARSE
            // ring around that, then the flat horizon. To avoid T-junction cracks where two resolutions meet
            // (the world-space waves would tear a straight coarse edge), the rings OVERLAP and are stacked in Y —
            // the finer ring sits slightly higher and simply draws on top of the coarser one it covers.
            //   LOD0 fine:  4×4 × 120m = 480m, res 90, y  0.00
            //   LOD1 coarse: 5×5 × 480m = 2400m, res 40, y -0.20 (continuous sheet UNDER LOD0, no gap)
            //   Horizon:    fieldSize×2.4, res 40, y -0.35

            // res kept deliberately LOW (big triangles) for the Poseidon-style low-poly faceted water — the flat
            // per-triangle shading in IslandSystem/Water only reads as "polygons" when the tris are large enough.
            var lod0 = BuildWaterRing("Water LOD0 (fine)", ocean.transform, 120f, 30, 4, 0f, addReflection: true);
            var lod1 = BuildWaterRing("Water LOD1 (coarse)", ocean.transform, 480f, 40, 5, -0.20f, addReflection: false);

            float horizonSize = size * 2.4f;
            var horizon = new GameObject("Water Horizon");
            horizon.transform.SetParent(ocean.transform, false);
            horizon.transform.localPosition = new Vector3(-horizonSize * 0.5f, -0.35f, -horizonSize * 0.5f);
            var twh = horizon.AddComponent<Pinwheel.Poseidon.TileableWater>();
            twh.material = waterMaterial;
            twh.meshPattern = Pinwheel.Poseidon.PlaneMeshPattern.Hexagon;
            twh.tileMeshDesc = new Pinwheel.Poseidon.TileMeshDesc
            { size = horizonSize, resolution = 40, needNormals = false, needTangents = false };
            twh.GetOrAddTile(0, 0);
            twh.GenerateMesh();

            // -- tides (slow vertical swing of the whole ocean) ----------------------------------------
            var tide = ocean.AddComponent<OceanTide>();
            tide.seaLevel = waterLevel;
            tide.amplitude = 1.5f;
            tide.period = 120f;

            // Regenerate the Poseidon tile meshes a frame later — GenerateMesh() called in this same frame (above)
            // yields empty tiles because the components haven't initialised yet.
            ocean.AddComponent<OceanWaterInit>();

            // Hide the whole ocean (and its costly planar-reflection pass) when the viewer is down inside an island
            // with no sea in view — the water under the landmass is pure waste there. See OceanVisibilityGate.
            var gate = ocean.AddComponent<OceanVisibilityGate>();
            gate.waterLevel = waterLevel;
            gate.rings = new[] { lod0.transform, lod1.transform, horizon.transform };
        }

        /// <summary>One viewer-following LOD ring: a count×count grid of hexagon water tiles at the given tile
        /// size/resolution, held at local Y <paramref name="y"/> (rings stack in Y to hide res-boundary cracks).</summary>
        Pinwheel.Poseidon.TileableWater BuildWaterRing(string name, Transform parent, float tileSize, int resolution,
            int count, float y, bool addReflection)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            var tw = go.AddComponent<Pinwheel.Poseidon.TileableWater>();
            tw.material = waterMaterial;
            tw.meshPattern = Pinwheel.Poseidon.PlaneMeshPattern.Hexagon;
            tw.tileMeshDesc = new Pinwheel.Poseidon.TileMeshDesc
            { size = tileSize, resolution = resolution, needNormals = false, needTangents = false };
            int half = count / 2;
            for (int tz = -half; tz < count - half; tz++)
                for (int tx = -half; tx < count - half; tx++)
                    tw.GetOrAddTile(tx, tz);
            tw.GenerateMesh();

            go.AddComponent<OceanFollowViewer>().snap = tileSize;   // follows the viewer, keeps its local Y offset

            if (addReflection)
            {
                var refl = go.AddComponent<Pinwheel.Poseidon.PlanarReflectionRenderer>();
                refl.textureResolution = 256;   // reflection is a blurry surface mirror — 256 is plenty, half the fill
                // Reflect only sky + terrain silhouette. EXCLUDE Water/UI and — crucially — Foliage: mirroring
                // every tree of the island behind you roughly doubled the triangle load when facing the sea.
                int foliage = LayerMask.NameToLayer("Foliage");
                int mask = (1 << LayerMask.NameToLayer("Water")) | (1 << LayerMask.NameToLayer("UI"));
                if (foliage >= 0) mask |= (1 << foliage);
                refl.reflectionLayers = ~mask;
            }
            return tw;
        }

        // ---- Lakes ----

        static Mesh _lakePlane;
        /// <summary>Shared SUBDIVIDED unit DISC (radius 1, concentric rings) — the rings give the water shader
        /// vertices to displace for waves, and it's BOUNDED (no square corners) so it stays within the basin's
        /// surrounding terrain, which clips it to the natural lake shape (no visible circle edge).</summary>
        static Mesh LakeWaterMesh()
        {
            if (_lakePlane != null) return _lakePlane;
            const int rings = 12, seg = 40;
            var verts = new System.Collections.Generic.List<Vector3>(1 + rings * seg);
            var uvs = new System.Collections.Generic.List<Vector2>(1 + rings * seg);
            var tris = new System.Collections.Generic.List<int>();
            verts.Add(Vector3.zero); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int r = 1; r <= rings; r++)
            {
                float rad = (float)r / rings;
                for (int s = 0; s < seg; s++)
                {
                    float a = Mathf.PI * 2f * s / seg;
                    verts.Add(new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad));
                    uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a) * rad, 0.5f + 0.5f * Mathf.Sin(a) * rad));
                }
            }
            // centre fan (viewed from +Y, wind CCW so normals point up)
            for (int s = 0; s < seg; s++) { int a = 1 + s, b = 1 + (s + 1) % seg; tris.Add(0); tris.Add(b); tris.Add(a); }
            for (int r = 1; r < rings; r++)
            {
                int inner = 1 + (r - 1) * seg, outer = 1 + r * seg;
                for (int s = 0; s < seg; s++)
                {
                    int i0 = inner + s, i1 = inner + (s + 1) % seg, o0 = outer + s, o1 = outer + (s + 1) % seg;
                    tris.Add(i0); tris.Add(o1); tris.Add(o0);
                    tris.Add(i0); tris.Add(i1); tris.Add(o1);
                }
            }
            _lakePlane = new Mesh { name = "LakeWaterDisc" };
            _lakePlane.SetVertices(verts); _lakePlane.SetUVs(0, uvs); _lakePlane.SetTriangles(tris, 0);
            _lakePlane.RecalculateNormals(); _lakePlane.RecalculateBounds();
            return _lakePlane;
        }

        /// <summary>Wavy water plane filling a lake basin at its (possibly above-sea) water level. It's a flat plane
        /// bigger than the basin CLIPPED by the surrounding terrain (which rises above the water), so it reads like
        /// the ocean — natural-shaped, with the shader's small waves — not a flat disc. Terrain-child space.</summary>
        void CreateLakeWater(Transform terrainGO, IslandTerrainGenerator.LakeZone lake, Vector3 size)
        {
            var mat = lakeMaterial != null ? lakeMaterial : waterMaterial;
            if (mat == null) return;
            var go = new GameObject("Lake");
            go.transform.SetParent(terrainGO, false);
            go.transform.localPosition = new Vector3(lake.centerUV.x * size.x, lake.waterLevelNormalized * size.y, lake.centerUV.y * size.z);
            float cover = Mathf.Max(lake.radiusWorld + 2f, lake.coverRadiusWorld);   // spans the shore; terrain clips it
            go.transform.localScale = new Vector3(cover, 1f, cover);
            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer >= 0) go.layer = waterLayer;
            go.AddComponent<MeshFilter>().sharedMesh = LakeWaterMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>Rebuild ONLY the ocean water (native Poseidon setup) without regenerating the islands.</summary>
        [ContextMenu("Rebuild Ocean")]
        public void RebuildOcean()
        {
            var root = transform.Find(RootName);
            if (root == null) { Debug.LogWarning("[Archipelago] No generated archipelago found.", this); return; }
            var old = root.Find("Ocean");
            float size = oceanSize;
            var seabed = root.Find("Seabed");
            if (seabed != null) size = seabed.localScale.x * 10f / 2.4f;   // recover fieldSize from the seabed span
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }
            CreateOcean(root, size);
        }

        void CleanGeneratedTerrain()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) return; // runtime terrains are in-memory; nothing on disk to clean
            // Delete previously generated TerrainData so changing island counts never leaks assets.
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
            if (Application.isPlaying) return new Material(shader) { name = "URP_Terrain" }; // runtime: in-memory
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
