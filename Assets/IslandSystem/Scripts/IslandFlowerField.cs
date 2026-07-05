using System.Collections.Generic;
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

        [Tooltip("Flowers cast shadows. Off is cheaper if the field is dense.")]
        public bool castShadows = true;

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
            var data = terrain.terrainData;
            // Accumulate world matrices per (mesh, submesh, material) so each becomes ONE instanced draw.
            var acc = new Dictionary<(Mesh, int, Material), List<Matrix4x4>>();
            var bounds = new Bounds();
            bool anyBounds = false;

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

                    float density = rule.density > 0f ? rule.density : 1f;
                    float spacing = 10f / Mathf.Sqrt(density);
                    int gx = Mathf.Max(1, Mathf.CeilToInt((uMax - uMin) * size.x / spacing));
                    int gz = Mathf.Max(1, Mathf.CeilToInt((vMax - vMin) * size.z / spacing));
                    var rng = new System.Random((cseed + bi * 131) * 48271 ^ ruleIndex * 28657);
                    const float jitter = 0.8f;

                    for (int iz = 0; iz < gz; iz++)
                    {
                        for (int ix = 0; ix < gx; ix++)
                        {
                            float u = Mathf.Clamp01(Mathf.Lerp(uMin, uMax, (ix + 0.5f + ((float)rng.NextDouble() - 0.5f) * jitter) / gx));
                            float v = Mathf.Clamp01(Mathf.Lerp(vMin, vMax, (iz + 0.5f + ((float)rng.NextDouble() - 0.5f) * jitter) / gz));
                            if (IslandTerrainGenerator.InAnyVillage(u, v, size, marker.villages)) continue;
                            float h01 = data.GetInterpolatedHeight(u, v) / Mathf.Max(0.0001f, size.y);
                            if (h01 < FadeThreshold || h01 < band.lo || h01 > band.hi) continue;
                            float slope = data.GetSteepness(u, v);
                            if ((float)rng.NextDouble() > IslandTerrainGenerator.ConditionWeight(rule.where, h01, slope)) continue;

                            // One CLUSTER: first flower at the centre, the rest uniform in the disc.
                            int count = rng.Next(rule.clusterSize.x, rule.clusterSize.y + 1);
                            float ccx = u * size.x, ccz = v * size.z;
                            for (int i = 0; i < count; i++)
                            {
                                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                                float rad = i == 0 ? 0f : rule.clusterRadius * Mathf.Sqrt((float)rng.NextDouble());
                                float wx = ccx + Mathf.Cos(ang) * rad;
                                float wz = ccz + Mathf.Sin(ang) * rad;
                                float fu = Mathf.Clamp01(wx / size.x), fv = Mathf.Clamp01(wz / size.z);
                                float fh01 = data.GetInterpolatedHeight(fu, fv) / Mathf.Max(0.0001f, size.y);
                                if (fh01 < FadeThreshold || fh01 < band.lo || fh01 > band.hi) continue;
                                if (IslandTerrainGenerator.InAnyVillage(fu, fv, size, marker.villages)) continue;

                                int idx = IslandTerrainGenerator.PickWeighted(rule.prefabWeights, rule.prefabs.Length, rng);
                                var prefab = rule.prefabs[idx];
                                if (prefab == null) continue;
                                float s = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                                if (s <= 0f) s = 1f;

                                Vector3 worldPos = origin + new Vector3(wx, data.GetInterpolatedHeight(fu, fv) - rule.sink, wz);
                                Quaternion rot = rule.alignToNormal
                                    ? Quaternion.FromToRotation(Vector3.up, data.GetInterpolatedNormal(fu, fv))
                                    : Quaternion.identity;
                                if (rule.randomYRotation)
                                    rot = rot * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                                // Instance matrix per renderable part (folds the prefab-root scale × user scale).
                                Vector3 scaleVec = prefab.transform.localScale * s;
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
                        }
                    }
                }
            }

            if (acc.Count == 0) return null;
            bounds.Expand(6f);   // pad for the plant height + wind sway so the whole-chunk cull isn't too tight
            var batches = new Batch[acc.Count];
            int bIdx = 0;
            foreach (var kv in acc)
                batches[bIdx++] = new Batch { mesh = kv.Key.Item1, submesh = kv.Key.Item2, material = kv.Key.Item3, matrices = kv.Value.ToArray(), bounds = bounds };
            return new FlowerChunk { batches = batches };
        }

        static long Key(int cx, int cz) => ((long)(cx + 100000) << 21) ^ (uint)(cz + 100000);
    }
}
