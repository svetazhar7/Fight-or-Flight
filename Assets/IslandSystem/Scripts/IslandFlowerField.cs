using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandSystem
{
    /// <summary>
    /// Streams FLOWER clusters around the viewer like the grass (<see cref="IslandGrassField"/>): chunks within
    /// <see cref="viewDistance"/> are BUILT only when the camera looks at them (frustum gate, inner ring always)
    /// and removed by distance. Each chunk is GPU-INSTANCED — the flower/bush prefab meshes are drawn with
    /// Graphics.RenderMeshInstanced (a few draw calls for thousands of flowers) instead of one GameObject each,
    /// which is what pushed the draw-call count into the thousands. They use the IslandSystem/Foliage shader
    /// (wind + smooth distance fade), on the Foliage layer (excluded from the water reflection). Per-chunk
    /// deterministic seed → identical flowers on every networked peer.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(IslandMarker))]
    public class IslandFlowerField : MonoBehaviour
    {
        public Terrain terrain;
        [Tooltip("Explicit viewer to stream around. Empty = local player (play) / Scene camera (edit).")]
        public Transform viewer;
        public int seed;
        [Tooltip("World size of a flower chunk (m).")]
        public float chunkSize = 32f;
        [Tooltip("Flowers exist within this radius of the viewer (m). The IslandSystem/Foliage shader fades " +
                 "them out just inside this so they appear/vanish smoothly instead of popping.")]
        public float viewDistance = 110f;
        [Tooltip("Max chunks built per update, so streaming in doesn't hitch.")]
        public int buildsPerTick = 3;

        [Tooltip("Flowers cast shadows. Off is cheaper if the field is dense — flower shadows are barely visible " +
                 "but push the whole foliage geometry through the shadow pass, so this defaults OFF.")]
        public bool castShadows = false;

        /// <summary>Same low-altitude gate the generation-time scatters use (no flowers on the sea fade).</summary>
        const float FadeThreshold = 0.05f;
        // Foliage layer (excluded from the water reflection). Lazy — NameToLayer can't run in a field initializer.
        static int _foliageLayer = -2;
        static int FoliageLayer { get { if (_foliageLayer == -2) _foliageLayer = LayerMask.NameToLayer("Foliage"); return _foliageLayer; } }

        // Global shader fade band (IslandSystem/Foliage): flowers/bushes shrink into the ground across it, so
        // they dissolve in/out with distance like the grass. End sits just inside viewDistance so a plant is
        // fully gone before its chunk is ever removed.
        static readonly int FadeStartId = Shader.PropertyToID("_FoliageFadeStart");
        static readonly int FadeEndId = Shader.PropertyToID("_FoliageFadeEnd");

        // ---- instanced draw data ----
        struct Part { public Mesh mesh; public int submesh; public Material material; public Matrix4x4 local; }
        struct Batch { public Mesh mesh; public int submesh; public Material material; public Matrix4x4[] matrices; public Bounds bounds; }
        sealed class FlowerChunk { public Batch[] batches; }

        // The prefab's renderable parts (submeshes) with the mesh-local transform baked in — computed once per
        // prefab so instance matrices are just TRS(worldPos, rot, scale) * part.local (handles FBX ×100 etc.).
        static readonly Dictionary<GameObject, Part[]> _partCache = new Dictionary<GameObject, Part[]>();

        readonly Dictionary<long, FlowerChunk> _chunks = new Dictionary<long, FlowerChunk>();
        static readonly Plane[] _planes = new Plane[6];
        IslandMarker _marker;
        int _builtThisTick;

        // Native copy of this island's heightmap, sampled by the Burst FlowerScatterJob instead of managed
        // TerrainData calls. Built once (lazily), disposed on OnDisable.
        HeightField _hf;

        IslandMarker Marker => _marker != null ? _marker : (_marker = GetComponent<IslandMarker>());

        public static bool HasAnyFlowers(List<IslandBand> bands)
        {
            if (bands == null) return false;
            foreach (var b in bands)
            {
                if (b.biome == null || b.biome.flowerRules == null) continue;
                foreach (var r in b.biome.flowerRules) if (r != null && r.IsValid) return true;
            }
            return false;
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.update += EditorTick;
#endif
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            ClearAll();
            if (_hf.IsCreated) _hf.Dispose();
        }

        void ClearAll() => _chunks.Clear();   // instanced draw data only — no GameObjects to destroy

        // ---- draw the streamed chunks, GPU-instanced, per camera ----
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (_chunks.Count == 0) return;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            var shadow = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            int layer = FoliageLayer >= 0 ? FoliageLayer : 0;

            foreach (var kv in _chunks)
            {
                var chunk = kv.Value; if (chunk == null || chunk.batches == null) continue;
                for (int i = 0; i < chunk.batches.Length; i++)
                {
                    var b = chunk.batches[i];
                    if (b.matrices == null || b.matrices.Length == 0 || b.mesh == null || b.material == null) continue;
                    if (!GeometryUtility.TestPlanesAABB(_planes, b.bounds)) continue;   // whole-chunk cull

                    var rp = new RenderParams(b.material)
                    { camera = cam, worldBounds = b.bounds, shadowCastingMode = shadow, receiveShadows = true, layer = layer };
                    for (int start = 0; start < b.matrices.Length; start += 1023)
                        Graphics.RenderMeshInstanced(rp, b.mesh, b.submesh, b.matrices, Mathf.Min(1023, b.matrices.Length - start), start);
                }
            }
        }

        static Part[] GetParts(GameObject prefab)
        {
            if (_partCache.TryGetValue(prefab, out var cached)) return cached;
            var list = new List<Part>();
            var root = prefab.transform;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh; var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null) continue;
                Matrix4x4 local = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;   // bake FBX/child transform
                var mats = mr.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = (mats != null && mats.Length > 0) ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                    if (mat == null) continue;
                    mat.enableInstancing = true;
                    list.Add(new Part { mesh = mesh, submesh = s, material = mat, local = local });
                }
            }
            var arr = list.ToArray();
            _partCache[prefab] = arr;
            return arr;
        }

        void LateUpdate() { if (Application.isPlaying) Stream(ViewerPos(), ViewerCamera()); }

#if UNITY_EDITOR
        void EditorTick() { if (this == null || Application.isPlaying) return; Stream(ViewerPos(), ViewerCamera()); }
#endif

        Vector3 ViewerPos()
        {
            if (viewer != null) return viewer.position;
            if (Application.isPlaying && IslandGrassField.LocalViewer != null) return IslandGrassField.LocalViewer.position;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null) return sv.camera.transform.position;
            }
#endif
            var c = Camera.main;
            return c != null ? c.transform.position : transform.position;
        }

        Camera ViewerCamera()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null) return sv.camera;
            }
#endif
            if (IslandGrassField.LocalViewerCamera != null) return IslandGrassField.LocalViewerCamera;
            return Camera.main;
        }

        /// <summary>Force streaming around an explicit position (tests / cutscenes). Builds a full disk.</summary>
        public void RefreshAround(Vector3 v) { Stream(v, null); }

        void Stream(Vector3 viewerPos, Camera cam)
        {
            var marker = Marker;
            if (terrain == null || marker == null || !HasAnyFlowers(marker.bands)) return;
            _builtThisTick = 0;

            // Drive the shader's smooth distance fade to match this field's radius (shrink to nothing before
            // the chunk-removal boundary at viewDistance + chunkSize, so nothing ever pops).
            Shader.SetGlobalFloat(FadeStartId, viewDistance * 0.6f);
            Shader.SetGlobalFloat(FadeEndId, viewDistance * 0.92f);

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            int cvx = Mathf.FloorToInt((viewerPos.x - origin.x) / chunkSize);
            int cvz = Mathf.FloorToInt((viewerPos.z - origin.z) / chunkSize);
            int r = Mathf.CeilToInt(viewDistance / chunkSize);
            float keepR2 = (viewDistance + chunkSize) * (viewDistance + chunkSize);
            float innerR2 = (2f * chunkSize) * (2f * chunkSize);

            // Frustum-gated BUILDING, distance-based KEEPING — same policy as the grass field.
            Plane[] planes = null;
            if (cam != null)
            {
                planes = GeometryUtility.CalculateFrustumPlanes(cam);
                for (int i = 0; i < planes.Length; i++)
                {
                    var pl = planes[i];
                    pl.distance += chunkSize * 0.75f;
                    planes[i] = pl;
                }
            }

            var want = new HashSet<long>();
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = cvx + dx, cz = cvz + dz;
                    float wcx = origin.x + (cx + 0.5f) * chunkSize, wcz = origin.z + (cz + 0.5f) * chunkSize;
                    float d2 = (wcx - viewerPos.x) * (wcx - viewerPos.x) + (wcz - viewerPos.z) * (wcz - viewerPos.z);
                    if (d2 > keepR2) continue;

                    float u0 = cx * chunkSize / size.x, u1 = (cx + 1) * chunkSize / size.x;
                    float v0 = cz * chunkSize / size.z, v1 = (cz + 1) * chunkSize / size.z;
                    if (u1 <= 0f || u0 >= 1f || v1 <= 0f || v0 >= 1f) continue;

                    long key = Key(cx, cz);
                    want.Add(key);
                    if (_chunks.ContainsKey(key)) continue;
                    if (_builtThisTick >= Mathf.Max(1, buildsPerTick)) continue;

                    if (planes != null && d2 > innerR2)
                    {
                        var bb = new Bounds(
                            new Vector3(wcx, origin.y + size.y * 0.5f, wcz),
                            new Vector3(chunkSize, size.y + 6f, chunkSize));
                        if (!GeometryUtility.TestPlanesAABB(planes, bb)) continue;
                    }

                    int cseed = seed ^ (cx * 73856093) ^ (cz * 19349663) ^ 0x466C7772; // 'Flwr' salt vs grass
                    _chunks[key] = BuildChunk(cx, cz, cseed, origin, size,
                        Mathf.Clamp01(u0), Mathf.Clamp01(u1), Mathf.Clamp01(v0), Mathf.Clamp01(v1));
                    _builtThisTick++;
                }
            }

            if (_chunks.Count > want.Count)
            {
                var stale = new List<long>();
                foreach (var kv in _chunks) if (!want.Contains(kv.Key)) stale.Add(kv.Key);
                foreach (var k in stale) _chunks.Remove(k);   // instanced data only; GC reclaims it
            }
        }

        /// <summary>
        /// Builds one chunk's flower clusters (may return null if nothing spawned): per band × rule, a
        /// jittered grid of cluster centres (density = CLUSTERS per 100 m²) gated by band / condition /
        /// village; every accepted centre spawns clusterSize flowers inside clusterRadius, each re-gated
        /// individually so patches trim at band edges and village rims.
        /// </summary>
        FlowerChunk BuildChunk(int cx, int cz, int cseed, Vector3 origin, Vector3 size,
            float uMin, float uMax, float vMin, float vMax)
        {
            var marker = Marker;
            if (terrain == null) return null;

            // Native heightmap copy, built once (rebuilt if the terrain resolution changes under us).
            if (_hf.IsCreated && _hf.res != terrain.terrainData.heightmapResolution) _hf.Dispose();
            if (!_hf.IsCreated) _hf = HeightField.FromTerrain(terrain, Allocator.Persistent);

            // Flatten this island's flower rules + prefabs/weights, baking the per-chunk RNG seed into each rule.
            var allPrefabs = new List<GameObject>();
            var weightList = new List<float>();
            var ruleList = new List<ScatterRuleB>();
            int bi = 0;
            foreach (var band in marker.bands)
            {
                bi++;
                var biome = band.biome;
                if (biome == null || biome.flowerRules == null) continue;
                int ruleIndex = 0;
                foreach (var rule in biome.flowerRules)
                {
                    ruleIndex++;
                    if (rule == null || !rule.IsValid || rule.prefabs == null || rule.prefabs.Length == 0) continue;

                    int start = allPrefabs.Count, wStart = weightList.Count, kept = 0;
                    for (int pi = 0; pi < rule.prefabs.Length; pi++)
                    {
                        var p = rule.prefabs[pi];
                        if (p == null) continue;
                        allPrefabs.Add(p);
                        weightList.Add(rule.prefabWeights != null && pi < rule.prefabWeights.Length ? rule.prefabWeights[pi] : 1f);
                        kept++;
                    }
                    if (kept == 0) continue;

                    ruleList.Add(new ScatterRuleB
                    {
                        where = rule.where,
                        bandLo = band.lo, bandHi = band.hi,
                        density = rule.density > 0f ? rule.density : 1f,
                        spacingVariation = 0f,
                        scaleRange = new float2(rule.scaleRange.x, rule.scaleRange.y),
                        heightRange = new float2(rule.heightScaleRange.x, rule.heightScaleRange.y),
                        sink = rule.sink,
                        alignToNormal = (byte)(rule.alignToNormal ? 1 : 0),
                        randomYRotation = (byte)(rule.randomYRotation ? 1 : 0),
                        nonUniformScale = 0,
                        prefabStart = start, prefabCount = kept,
                        weightStart = wStart, weightCount = kept,
                        clusterSize = new int2(rule.clusterSize.x, rule.clusterSize.y),
                        clusterRadius = rule.clusterRadius,
                        seed = (uint)((cseed + bi * 131) * 48271 ^ ruleIndex * 28657)
                    });
                }
            }
            if (ruleList.Count == 0) return null;

            // DOTS: place the chunk's flower clusters in a Burst job over the native heightmap.
            var villageArr = IslandTerrainGenerator.BuildVillageArray(marker.villages, Allocator.TempJob);
            var rulesArr = new NativeArray<ScatterRuleB>(ruleList.ToArray(), Allocator.TempJob);
            var weightsArr = new NativeArray<float>(weightList.ToArray(), Allocator.TempJob);
            var outList = new NativeList<ScatterInstance>(256, Allocator.TempJob);

            new FlowerScatterJob
            {
                hf = _hf, rules = rulesArr, weights = weightsArr, villages = villageArr,
                uMin = uMin, uMax = uMax, vMin = vMin, vMax = vMax, outList = outList
            }.Schedule().Complete();

            // Read the placements back and accumulate world matrices per (mesh, submesh, material) → one draw each.
            FlowerChunk chunk = null;
            if (outList.Length > 0)
            {
                var acc = new Dictionary<(Mesh, int, Material), List<Matrix4x4>>();
                var bounds = new Bounds();
                bool anyBounds = false;
                for (int i = 0; i < outList.Length; i++)
                {
                    var inst = outList[i];
                    var prefab = allPrefabs[inst.prefab];
                    if (prefab == null) continue;
                    Vector3 worldPos = (Vector3)inst.pos;
                    Quaternion rot = new Quaternion(inst.rot.value.x, inst.rot.value.y, inst.rot.value.z, inst.rot.value.w);
                    Vector3 scaleVec = Vector3.Scale(prefab.transform.localScale, (Vector3)inst.scale);
                    Matrix4x4 world = Matrix4x4.TRS(worldPos, rot, scaleVec);
                    foreach (var part in GetParts(prefab))
                    {
                        var key = (part.mesh, part.submesh, part.material);
                        if (!acc.TryGetValue(key, out var mats)) { mats = new List<Matrix4x4>(); acc[key] = mats; }
                        mats.Add(world * part.local);
                    }
                    if (!anyBounds) { bounds = new Bounds(worldPos, Vector3.zero); anyBounds = true; }
                    else bounds.Encapsulate(worldPos);
                }
                if (acc.Count > 0)
                {
                    bounds.Expand(6f);   // pad for plant height + wind sway so the whole-chunk cull isn't too tight
                    var batches = new Batch[acc.Count];
                    int bIdx = 0;
                    foreach (var kv in acc)
                        batches[bIdx++] = new Batch { mesh = kv.Key.Item1, submesh = kv.Key.Item2, material = kv.Key.Item3, matrices = kv.Value.ToArray(), bounds = bounds };
                    chunk = new FlowerChunk { batches = batches };
                }
            }

            villageArr.Dispose(); rulesArr.Dispose(); weightsArr.Dispose(); outList.Dispose();
            return chunk;
        }

        static long Key(int cx, int cz) => ((long)(cx + 100000) << 21) ^ (uint)(cz + 100000);
    }
}
