using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandSystem
{
    /// <summary>
    /// Draws an island's trees with GPU INSTANCING (Graphics.RenderMeshInstanced) instead of one GameObject per
    /// tree, plus two cheap optimizations so the triangle load scales with what you actually see:
    ///  • PER-INSTANCE FRUSTUM CULLING (occlusion): only trees inside the camera frustum are drawn, so standing
    ///    in the forest no longer renders the whole island's ring behind you.
    ///  • DISTANCE DENSITY LOD: past <see cref="lodNear"/> a stable, growing fraction of trees is dropped, down
    ///    to <see cref="lodMinKeep"/> at <see cref="lodFar"/> — distant / aerial forests stay readable but cost
    ///    a fraction of the triangles. (Angle-independent — works from above too; no impostor baking.)
    /// Placement is serialized per species so it survives domain reloads and scene save/load; every peer that
    /// regenerates from the seed gets identical trees.
    /// </summary>
    [ExecuteAlways]
    public class IslandTreeRenderer : MonoBehaviour
    {
        /// <summary>Serialized placement for one tree species (prefab) — parallel pos/rot/scale lists.</summary>
        [System.Serializable]
        public class Species
        {
            public GameObject prefab;
            public List<Vector3> positions = new List<Vector3>();
            public List<Quaternion> rotations = new List<Quaternion>();
            public List<Vector3> scales = new List<Vector3>();   // already includes the prefab-root scale × user scale
        }

        [SerializeField] List<Species> species = new List<Species>();

        [Header("Shadows")]
        public bool castShadows = true;
        [Tooltip("STABLE SHADOWS: trees cast shadows from a SEPARATE, yaw-independent draw culled only by distance " +
                 "to the camera (never by the camera frustum, LOD thinning, terrain occlusion, or impostor swap). " +
                 "That is what keeps tree-crown shadows from crawling / rebuilding while the player walks and turns. " +
                 "0 = auto: follow the URP shadow distance so the caster set matches exactly what the shadow map covers.")]
        public float shadowCasterRange = 0f;
        [Tooltip("Extra metres added to the shadow caster radius so a tree whose TRUNK is just past the shadow " +
                 "distance still drops its long low-sun canopy shadow into the visible range.")]
        [Range(0f, 60f)] public float shadowRangeMargin = 20f;

        [Header("Distance density LOD (metres)")]
        [Tooltip("Full detail within this distance.")]
        public float lodNear = 220f;
        [Tooltip("At this distance only lodMinKeep of the trees remain.")]
        public float lodFar = 800f;
        // Kept at 0.55 (not the old 0.2): thinning to 20% made distant islands read as FIR-ONLY — the smaller,
        // less-numerous deciduous trees dropped out and their mid-green canopies blend into the grass, so only the
        // big dark firs stayed visible. 0.55 keeps the mixed canopy full at range (perf: more distant instanced
        // trees, but they're frustum-culled, wind-static past 150 m, and tiny on screen). Tune down if FPS needs it.
        [Tooltip("Fraction of trees kept at/after lodFar (0.55 = 55%).")]
        [Range(0.05f, 1f)] public float lodMinKeep = 0.55f;

        [Header("Impostors (far tree LOD)")]
        [Tooltip("Beyond this distance (m) a tree draws as a cheap camera-facing IMPOSTOR billboard (baked from a " +
                 "hemisphere of angles, so it looks right from the side AND from above) instead of its ~2000-tri " +
                 "mesh — the big FPS win for aerial / deep-forest views. 0 disables impostors (mesh at all distances).")]
        public float impostorDistance = 140f;

        [Header("Trunk colliders")]
        [Tooltip("Give tree trunks capsule colliders. Streamed/pooled around the player (physics only in Play) so " +
                 "there is no per-tree GameObject — only the handful of trees near you actually have a collider.")]
        public bool generateColliders = true;
        [Tooltip("Trunk collider radius for a scale-1 tree (m). Scaled per instance.")]
        public float trunkRadius = 0.5f;
        [Tooltip("Trees within this horizontal distance of the player get a capsule collider.")]
        public float colliderDistance = 35f;
        [Tooltip("Safety cap on how many trunk colliders exist at once (nearest trees are prioritized).")]
        public int maxColliders = 200;

        // Runtime draw data (rebuilt from the serialized species; not serialized itself). Per-instance data lives
        // in NativeArrays so the cull runs in a Burst job (TreeCullJob); `vis` is that job's output list, handed
        // straight to RenderMeshInstanced.
        struct Batch
        {
            public Mesh mesh; public int submesh; public Material material;
            public NativeArray<Matrix4x4> matrices; public NativeArray<float3> centers; public NativeArray<float> rand;
            public float radius; public float topOffset; public Bounds bounds; public NativeList<Matrix4x4> vis;
            public NativeList<Matrix4x4> visSh;   // SHADOW casters: separate, yaw-independent distance cull (stable set)
        }
        readonly List<Batch> _batches = new List<Batch>();
        static readonly Plane[] _planes = new Plane[6];
        Terrain _terrain;

        // ---- Burst cull scratch (persistent, disposed in OnDisable) ----
        HeightField _hf;                       // native island height for the occlusion sight-line test
        NativeArray<float4> _planesNA;         // 6 frustum planes, refilled per camera
        NativeList<JobHandle> _cullHandles;    // one handle per batch scheduled this frame
        readonly List<int> _scheduledBatches = new List<int>();
        readonly List<int> _scheduledShadow = new List<int>();   // batches with a shadow-caster cull scheduled this frame

        // ---- far-LOD impostors (one billboard set per species) ----
        struct ImpostorSet
        {
            public GameObject prefab; public Material material;
            public NativeArray<Matrix4x4> matrices;   // billboard TRS(base, identity, scale) per TREE
            public NativeArray<float3> centers; public NativeArray<float> rand;
            public float radius; public Bounds bounds; public NativeList<Matrix4x4> vis;
        }
        readonly List<ImpostorSet> _impostors = new List<ImpostorSet>();
        readonly List<int> _scheduledImp = new List<int>();
        static Mesh _quad;                                         // shared unit billboard quad (XY -0.5..0.5)
        struct ImpostorBake { public Material material; public Vector3 size; public Vector3 center; }
        static readonly Dictionary<GameObject, ImpostorBake> _bakeCache = new Dictionary<GameObject, ImpostorBake>();

        // ---- streamed trunk colliders (pooled, no per-tree GameObject) ----
        struct Trunk { public Vector3 basePos; public float height; public float radius; }
        readonly List<Trunk> _trunks = new List<Trunk>();
        Transform _colliderHolder;
        readonly List<CapsuleCollider> _colliderPool = new List<CapsuleCollider>();
        Vector3 _lastColliderPos = new Vector3(1e9f, 1e9f, 1e9f);
        static readonly List<int> _nearTrunks = new List<int>();

        [Header("Terrain occlusion (hide trees behind the hill)")]
        [Tooltip("Cull trees the island's own terrain hides (e.g. the far slope behind the peak). Off-screen " +
                 "frustum culling can't do this — the hidden trees are still in the frustum.")]
        public bool terrainOcclusion = true;
        [Tooltip("Don't bother testing trees closer than this (nothing occludes them at point blank).")]
        public float occlusionMinDistance = 55f;
        [Tooltip("A ridge must poke this many metres above the sight-line before a tree behind it is culled.")]
        public float occlusionBias = 2f;
        // Drawn on the "Foliage" layer so the water's planar-reflection camera can exclude it (reflecting every
        // tree of the island behind you roughly doubled the triangle load when facing the sea).
        static int _foliageLayer = -2;
        static int FoliageLayer { get { if (_foliageLayer == -2) _foliageLayer = LayerMask.NameToLayer("Foliage"); return _foliageLayer; } }
        // If a tree-leaf material uses the IslandSystem/Foliage shader (for wind sway), disable its streaming
        // distance-fade — that fade is meant for streamed flowers/bushes and would make tree canopies vanish far away.
        static readonly int UseFoliageFadeId = Shader.PropertyToID("_UseFoliageFade");
        bool _built;

        // ---- build API (called by IslandTerrainGenerator.PlaceTrees) ----

        public void BeginBuild() { species.Clear(); _batches.Clear(); _built = false; }

        /// <summary>Record one tree instance. <paramref name="scale"/> must already fold in the prefab root scale.</summary>
        public void Add(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (prefab == null) return;
            Species sp = null;
            for (int i = 0; i < species.Count; i++) if (species[i].prefab == prefab) { sp = species[i]; break; }
            if (sp == null) { sp = new Species { prefab = prefab }; species.Add(sp); }
            sp.positions.Add(position);
            sp.rotations.Add(rotation);
            sp.scales.Add(scale);
        }

        public void Finish() { Rebuild(); TryBakeImpostors(); }   // bake at generation time (safe, outside rendering)

        /// <summary>Append every placed instance's world position (used by the rock scatter to keep rocks off trees).</summary>
        public void CollectTreePositions(List<Vector3> into)
        {
            if (species == null || into == null) return;
            foreach (var sp in species) if (sp != null && sp.positions != null) into.AddRange(sp.positions);
        }

        // ---- rendering ----

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            // Defer the (re)build to the first camera render. Building here in OnEnable can run before the
            // serialized `species` list is fully restored after a domain reload, leaving empty batches and
            // invisible trees in the editor; by the first beginCameraRendering the scene is fully deserialized.
            _built = false;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (_colliderHolder != null)
            {
                if (Application.isPlaying) Destroy(_colliderHolder.gameObject);
                else DestroyImmediate(_colliderHolder.gameObject);
                _colliderHolder = null;
                _colliderPool.Clear();
            }

            // Release all Burst-cull native memory.
            DisposeBatches();
            _batches.Clear();
            if (_planesNA.IsCreated) _planesNA.Dispose();
            if (_cullHandles.IsCreated) _cullHandles.Dispose();
            _hf.Dispose();
            _hf = default;
            _built = false;
        }

        // ---- trunk collider streaming ----

        void LateUpdate()
        {
            // Bake impostor atlases here (outside camera rendering) if a domain reload / lazy rebuild left them missing.
            if (_built && impostorDistance > 0.01f && !ImpostorsReady()) TryBakeImpostors();
            if (!Application.isPlaying || !generateColliders) return;
            var viewer = ViewerTransform();
            if (viewer != null) StreamColliders(viewer.position);
        }

        /// <summary>Force trunk colliders to stream around an explicit point (tests / cutscenes).</summary>
        public void RefreshCollidersAround(Vector3 p) { if (generateColliders) StreamColliders(p, true); }

        static Transform ViewerTransform()
        {
            if (IslandGrassField.LocalViewer != null) return IslandGrassField.LocalViewer;
            var c = Camera.main;
            return c != null ? c.transform : null;
        }

        void StreamColliders(Vector3 vp, bool force = false)
        {
            if (!_built) Rebuild();
            if (_trunks.Count == 0) return;
            // Throttle: only re-solve when the viewer has moved a bit (the pool is otherwise stable).
            if (!force && _colliderHolder != null)
            {
                float mdx = vp.x - _lastColliderPos.x, mdz = vp.z - _lastColliderPos.z;
                if (mdx * mdx + mdz * mdz < 4f) return;   // < 2 m
            }
            _lastColliderPos = vp;

            float d2max = colliderDistance * colliderDistance;
            _nearTrunks.Clear();
            for (int i = 0; i < _trunks.Count; i++)
            {
                float dx = _trunks[i].basePos.x - vp.x, dz = _trunks[i].basePos.z - vp.z;
                if (dx * dx + dz * dz <= d2max) _nearTrunks.Add(i);
            }
            // If more trees are in range than the cap, keep the NEAREST ones (so the player never phases through
            // a tree right next to them just because a distant one claimed the slot).
            if (_nearTrunks.Count > maxColliders)
            {
                Vector3 v = vp;
                _nearTrunks.Sort((a, b) =>
                {
                    float da = Sq(_trunks[a].basePos, v), db = Sq(_trunks[b].basePos, v);
                    return da.CompareTo(db);
                });
                _nearTrunks.RemoveRange(maxColliders, _nearTrunks.Count - maxColliders);
            }

            EnsurePool(_nearTrunks.Count);
            for (int k = 0; k < _colliderPool.Count; k++)
            {
                var col = _colliderPool[k];
                if (k < _nearTrunks.Count)
                {
                    var tr = _trunks[_nearTrunks[k]];
                    var t = col.transform;
                    t.SetPositionAndRotation(tr.basePos, Quaternion.identity);
                    col.height = tr.height;
                    col.radius = tr.radius;
                    col.center = new Vector3(0f, tr.height * 0.5f, 0f);
                    if (!col.gameObject.activeSelf) col.gameObject.SetActive(true);
                }
                else if (col.gameObject.activeSelf) col.gameObject.SetActive(false);
            }
        }

        void EnsurePool(int count)
        {
            if (_colliderHolder == null)
            {
                var go = new GameObject("TrunkColliders") { hideFlags = HideFlags.DontSave };
                _colliderHolder = go.transform;
                _colliderHolder.SetParent(transform, false);
            }
            while (_colliderPool.Count < count && _colliderPool.Count < maxColliders)
            {
                var go = new GameObject("TrunkCollider") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(_colliderHolder, false);
                // Kinematic rigidbody so repeatedly moving the collider (as trees stream in/out) is cheap — moving
                // a plain static collider forces PhysX to rebuild its static broadphase every time.
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                var col = go.AddComponent<CapsuleCollider>();
                col.direction = 1;   // Y-axis capsule (vertical trunk)
                _colliderPool.Add(col);
            }
        }

        /// <summary>Combined mesh height of the prefab in its own local space (for the capsule length).</summary>
        static float PrefabHeight(GameObject prefab)
        {
            var root = prefab.transform;
            Bounds b = default; bool any = false;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Vector3 c = mf.sharedMesh.bounds.center, e = mf.sharedMesh.bounds.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            Vector3 p = m.MultiplyPoint3x4(c + new Vector3(e.x * sx, e.y * sy, e.z * sz));
                            if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                        }
            }
            return any ? Mathf.Max(1f, b.size.y) : 3f;
        }

        static float Sq(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

        void Rebuild()
        {
            DisposeBatches();
            _batches.Clear();
            _trunks.Clear();
            _built = true;
            if (species == null) return;

            foreach (var sp in species)
            {
                if (sp != null && sp.prefab != null && sp.positions.Count > 0)
                {
                    float hPrefab = PrefabHeight(sp.prefab);
                    for (int i = 0; i < sp.positions.Count; i++)
                    {
                        Vector3 sc = sp.scales[i];
                        _trunks.Add(new Trunk
                        {
                            basePos = sp.positions[i],
                            height = Mathf.Max(1f, hPrefab * Mathf.Abs(sc.y)),
                            radius = Mathf.Max(0.1f, trunkRadius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.z)))
                        });
                    }
                }
            }

            foreach (var sp in species)
            {
                if (sp == null || sp.prefab == null || sp.positions.Count == 0) continue;
                var root = sp.prefab.transform;

                foreach (var mf in sp.prefab.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mesh == null || mr == null) continue;

                    Matrix4x4 partLocal = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var sharedMats = mr.sharedMaterials;
                    int n = sp.positions.Count;
                    // A generous per-species bounding radius (mesh extent × the biggest instance) for the sphere cull.
                    float maxScale = 0f; for (int i = 0; i < n; i++) maxScale = Mathf.Max(maxScale, sp.scales[i].x, sp.scales[i].y, sp.scales[i].z);
                    float radius = mesh.bounds.extents.magnitude * Mathf.Max(0.01f, maxScale) + 1f;
                    float topOffset = mesh.bounds.size.y * Mathf.Max(0.01f, maxScale);   // canopy height for the sight-line

                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var mat = (sharedMats != null && sharedMats.Length > 0)
                            ? sharedMats[Mathf.Min(s, sharedMats.Length - 1)] : null;
                        if (mat == null) continue;
                        mat.enableInstancing = true;
                        // Tree foliage must never distance-fade like streamed flowers/bushes.
                        if (mat.HasProperty(UseFoliageFadeId)) mat.SetFloat(UseFoliageFadeId, 0f);

                        var matrices = new NativeArray<Matrix4x4>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        var centers = new NativeArray<float3>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        var rand = new NativeArray<float>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        var b = new Bounds(sp.positions[0], Vector3.zero);
                        for (int i = 0; i < n; i++)
                        {
                            matrices[i] = Matrix4x4.TRS(sp.positions[i], sp.rotations[i], sp.scales[i]) * partLocal;
                            Vector3 p = sp.positions[i];
                            centers[i] = new float3(p.x, p.y, p.z);
                            rand[i] = Hash01((uint)i * 2654435761u);   // per-TREE (shared by all submeshes + the impostor)
                            b.Encapsulate(p);
                        }
                        b.Expand(radius * 2f);
                        _batches.Add(new Batch
                        {
                            mesh = mesh, submesh = s, material = mat,
                            matrices = matrices, centers = centers, rand = rand,
                            radius = radius, topOffset = topOffset, bounds = b,
                            vis = new NativeList<Matrix4x4>(n, Allocator.Persistent),
                            visSh = new NativeList<Matrix4x4>(n, Allocator.Persistent)
                        });
                    }
                }

                // Far-LOD impostor set for this species (one billboard per TREE; atlas material baked lazily).
                if (impostorDistance > 0.01f)
                {
                    _bakeCache.TryGetValue(sp.prefab, out var bake);
                    int nt = sp.positions.Count;
                    var im = new NativeArray<Matrix4x4>(nt, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    var ic = new NativeArray<float3>(nt, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    var ir = new NativeArray<float>(nt, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    var ib = new Bounds(sp.positions[0], Vector3.zero);
                    float maxS = 0f;
                    for (int i = 0; i < nt; i++)
                    {
                        Vector3 p = sp.positions[i], sc = sp.scales[i];
                        im[i] = Matrix4x4.TRS(p, Quaternion.identity, sc);
                        ic[i] = new float3(p.x, p.y, p.z);
                        ir[i] = Hash01((uint)i * 2654435761u);
                        ib.Encapsulate(p);
                        maxS = Mathf.Max(maxS, sc.x, sc.y, sc.z);
                    }
                    float irad = Mathf.Max(bake.size.x, Mathf.Max(bake.size.y, bake.size.z)) * Mathf.Max(0.01f, maxS) + 1f;
                    if (irad < 2f) irad = 30f;   // size unknown until the atlas is baked; use a generous cull bound
                    ib.Expand(irad * 2f);
                    _impostors.Add(new ImpostorSet
                    {
                        prefab = sp.prefab, material = bake.material,
                        matrices = im, centers = ic, rand = ir, radius = irad, bounds = ib,
                        vis = new NativeList<Matrix4x4>(nt, Allocator.Persistent)
                    });
                }
            }
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            // Skip cameras that don't even render the Foliage layer (e.g. the water's planar-reflection camera) —
            // no point culling trees they'll discard by layer mask anyway.
            if (FoliageLayer >= 0 && (cam.cullingMask & (1 << FoliageLayer)) == 0) return;
            if (!_built) Rebuild();          // lazy build once serialized species are guaranteed present
            if (_batches.Count == 0 && _impostors.Count == 0) return;

            if (_terrain == null) _terrain = GetComponent<Terrain>();
            EnsureCullNative();

            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            for (int p = 0; p < 6; p++)
            {
                Vector3 nrm = _planes[p].normal;
                _planesNA[p] = new float4(nrm.x, nrm.y, nrm.z, _planes[p].distance);
            }

            Vector3 camPos = cam.transform.position;
            var camPosF = new float3(camPos.x, camPos.y, camPos.z);
            float span = Mathf.Max(1f, lodFar - lodNear);
            float near2 = lodNear * lodNear;
            float occlMin2 = occlusionMinDistance * occlusionMinDistance;
            int doOcclusion = (terrainOcclusion && _hf.IsCreated) ? 1 : 0;

            // MESH renders NEAR, IMPOSTORS render FAR. Split at impostorDistance (once ready); until the atlases are
            // baked, mesh renders everything so nothing disappears.
            bool impOn = ImpostorsReady();
            float imp2 = impostorDistance * impostorDistance;
            float meshMax = impOn ? imp2 : float.MaxValue;

            // Schedule all cull jobs (mesh + impostor) then wait ONCE — the ~34k-instance work runs on worker threads.
            _scheduledBatches.Clear();
            _scheduledImp.Clear();
            _scheduledShadow.Clear();
            _cullHandles.Clear();

            // SHADOW caster radius: follow the URP shadow distance so the stable caster set matches exactly the
            // ground the shadow map covers (nothing drawn that can't land on screen; nothing missing that can).
            float shadowR = shadowCasterRange > 0.01f ? shadowCasterRange : 150f;
            if (shadowCasterRange <= 0.01f)
            {
                var urp = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
                if (urp != null) shadowR = urp.shadowDistance;
            }
            shadowR += shadowRangeMargin;
            float shadowR2 = shadowR * shadowR;
            bool doShadowPass = castShadows && shadowR2 > 1f;

            for (int bi = 0; bi < _batches.Count; bi++)
            {
                var b = _batches[bi];
                if (b.mesh == null || b.material == null || !b.matrices.IsCreated) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, b.bounds)) continue;   // whole island cloud off-screen

                b.vis.Clear();
                var job = new TreeCullJob
                {
                    matrices = b.matrices, centers = b.centers, rand = b.rand, planes = _planesNA,
                    camPos = camPosF, radius = b.radius, topOffset = b.topOffset,
                    lodNear = lodNear, span = span, lodMinKeep = lodMinKeep, near2 = near2,
                    minDist2 = 0f, maxDist2 = meshMax,
                    useFrustum = 1,
                    doOcclusion = doOcclusion, occlMin2 = occlMin2, occlusionBias = occlusionBias, hf = _hf,
                    outList = b.vis.AsParallelWriter()
                };
                _cullHandles.Add(job.Schedule(b.matrices.Length, 64));
                _scheduledBatches.Add(bi);
            }

            if (impOn)
            {
                for (int ii = 0; ii < _impostors.Count; ii++)
                {
                    var im = _impostors[ii];
                    if (im.material == null || !im.matrices.IsCreated) continue;
                    if (!GeometryUtility.TestPlanesAABB(_planes, im.bounds)) continue;

                    im.vis.Clear();
                    var job = new TreeCullJob
                    {
                        matrices = im.matrices, centers = im.centers, rand = im.rand, planes = _planesNA,
                        camPos = camPosF, radius = im.radius, topOffset = 0f,
                        lodNear = lodNear, span = span, lodMinKeep = lodMinKeep, near2 = near2,
                        minDist2 = imp2, maxDist2 = float.MaxValue,
                        useFrustum = 1,
                        doOcclusion = doOcclusion, occlMin2 = occlMin2, occlusionBias = occlusionBias, hf = _hf,
                        outList = im.vis.AsParallelWriter()
                    };
                    _cullHandles.Add(job.Schedule(im.matrices.Length, 64));
                    _scheduledImp.Add(ii);
                }
            }

            // SHADOW casters — a SEPARATE cull per batch using the FULL-DETAIL mesh, culled ONLY by distance to the
            // camera (useFrustum=0, near2=MAX so no LOD thinning, no terrain occlusion). Yaw-independent and
            // LOD-independent, so the set of shadow-casting trees does not change when the player turns or as a tree
            // crosses an LOD / impostor line — which is what stops the tree-crown shadows from crawling / rebuilding.
            if (doShadowPass)
            {
                for (int bi = 0; bi < _batches.Count; bi++)
                {
                    var b = _batches[bi];
                    if (b.mesh == null || b.material == null || !b.matrices.IsCreated) continue;
                    // Cheap batch reject: skip whole island clouds fully outside the shadow radius.
                    if (b.bounds.SqrDistance(camPos) > shadowR2) continue;

                    b.visSh.Clear();
                    var job = new TreeCullJob
                    {
                        matrices = b.matrices, centers = b.centers, rand = b.rand, planes = _planesNA,
                        camPos = camPosF, radius = b.radius, topOffset = 0f,
                        lodNear = lodNear, span = span, lodMinKeep = 1f, near2 = float.MaxValue,
                        minDist2 = 0f, maxDist2 = shadowR2,
                        useFrustum = 0,
                        doOcclusion = 0, occlMin2 = 0f, occlusionBias = 0f, hf = _hf,
                        outList = b.visSh.AsParallelWriter()
                    };
                    _cullHandles.Add(job.Schedule(b.matrices.Length, 64));
                    _scheduledShadow.Add(bi);
                }
            }

            if (_cullHandles.Length == 0) { if (!impOn) RequestEditorTick(); return; }
            JobHandle.CompleteAll(_cullHandles.AsArray());

            // draw near meshes
            for (int si = 0; si < _scheduledBatches.Count; si++)
            {
                var b = _batches[_scheduledBatches[si]];
                int vis = b.vis.Length;
                if (vis == 0) continue;

                var rp = new RenderParams(b.material)
                {
                    // Camera view does NOT cast — the stable shadow-only pass below owns shadow casting.
                    camera = cam, worldBounds = b.bounds,
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                    layer = FoliageLayer >= 0 ? FoliageLayer : 0
                };
                var arr = b.vis.AsArray();
                for (int start = 0; start < vis; start += 1023)
                    Graphics.RenderMeshInstanced(rp, b.mesh, b.submesh, arr, Mathf.Min(1023, vis - start), start);
            }

            // draw the stable shadow casters (ShadowsOnly — full mesh, distance-culled, no frustum/LOD/impostor churn)
            for (int si = 0; si < _scheduledShadow.Count; si++)
            {
                var b = _batches[_scheduledShadow[si]];
                int vis = b.visSh.Length;
                if (vis == 0) continue;

                var rp = new RenderParams(b.material)
                {
                    camera = cam, worldBounds = b.bounds,
                    shadowCastingMode = ShadowCastingMode.ShadowsOnly, receiveShadows = false,
                    layer = FoliageLayer >= 0 ? FoliageLayer : 0
                };
                var arr = b.visSh.AsArray();
                for (int start = 0; start < vis; start += 1023)
                    Graphics.RenderMeshInstanced(rp, b.mesh, b.submesh, arr, Mathf.Min(1023, vis - start), start);
            }

            // draw far impostor billboards
            if (_scheduledImp.Count > 0)
            {
                var quad = Quad();
                for (int si = 0; si < _scheduledImp.Count; si++)
                {
                    var im = _impostors[_scheduledImp[si]];
                    int vis = im.vis.Length;
                    if (vis == 0) continue;

                    var rp = new RenderParams(im.material)
                    {
                        camera = cam, worldBounds = im.bounds,
                        shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                        layer = FoliageLayer >= 0 ? FoliageLayer : 0
                    };
                    var arr = im.vis.AsArray();
                    for (int start = 0; start < vis; start += 1023)
                        Graphics.RenderMeshInstanced(rp, quad, 0, arr, Mathf.Min(1023, vis - start), start);
                }
            }

            if (!impOn) RequestEditorTick();
        }

        /// <summary>Allocate the persistent Burst-cull scratch (frustum planes, job handles, occlusion HeightField).</summary>
        void EnsureCullNative()
        {
            if (!_planesNA.IsCreated) _planesNA = new NativeArray<float4>(6, Allocator.Persistent);
            if (!_cullHandles.IsCreated) _cullHandles = new NativeList<JobHandle>(8, Allocator.Persistent);
            // The island heightfield is only needed for occlusion (~1 MB) — build it lazily, and only when on.
            if (terrainOcclusion && !_hf.IsCreated && _terrain != null)
                _hf = HeightField.FromTerrain(_terrain, Allocator.Persistent);
        }

        void DisposeBatches()
        {
            for (int i = 0; i < _batches.Count; i++)
            {
                var b = _batches[i];
                if (b.matrices.IsCreated) b.matrices.Dispose();
                if (b.centers.IsCreated) b.centers.Dispose();
                if (b.rand.IsCreated) b.rand.Dispose();
                if (b.vis.IsCreated) b.vis.Dispose();
                if (b.visSh.IsCreated) b.visSh.Dispose();
            }
            for (int i = 0; i < _impostors.Count; i++)
            {
                var im = _impostors[i];
                if (im.matrices.IsCreated) im.matrices.Dispose();
                if (im.centers.IsCreated) im.centers.Dispose();
                if (im.rand.IsCreated) im.rand.Dispose();
                if (im.vis.IsCreated) im.vis.Dispose();
            }
            _impostors.Clear();
        }

        static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "ImpostorQuad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
            return _quad;
        }

        /// <summary>True once every species' impostor atlas material is baked and ready to draw.</summary>
        bool ImpostorsReady()
        {
            if (impostorDistance <= 0.01f || _impostors.Count == 0) return false;
            for (int i = 0; i < _impostors.Count; i++) if (_impostors[i].material == null) return false;
            return true;
        }

        /// <summary>Bake any missing impostor atlases. MUST run outside camera rendering (LateUpdate / Finish) —
        /// the baker submits render requests, which can't nest inside beginCameraRendering. Atlases are cached per
        /// prefab (static), so it's ~one bake per species per domain load, shared across all island renderers.</summary>
        void TryBakeImpostors()
        {
            if (impostorDistance <= 0.01f) return;
            for (int i = 0; i < _impostors.Count; i++)
            {
                var im = _impostors[i];
                if (im.material != null || im.prefab == null) continue;
                if (!_bakeCache.TryGetValue(im.prefab, out var bake) || bake.material == null)
                {
                    var res = TreeImpostorBaker.Bake(im.prefab, 8, 128);
                    var mat = new Material(Shader.Find("IslandSystem/Impostor"));
                    mat.SetTexture("_Atlas", res.atlas);
                    mat.SetFloat("_Grid", res.grid);
                    mat.SetVector("_Size", res.boundsSize);
                    mat.SetVector("_CenterLocal", res.centerLocal);
                    mat.enableInstancing = true;
                    bake = new ImpostorBake { material = mat, size = res.boundsSize, center = res.centerLocal };
                    _bakeCache[im.prefab] = bake;
                }
                im.material = bake.material;
                _impostors[i] = im;
            }
        }

        void RequestEditorTick()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        static float Hash01(uint x)
        {
            x ^= x >> 16; x *= 2246822519u; x ^= x >> 13; x *= 3266489917u; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777215.0f;
        }
    }
}
