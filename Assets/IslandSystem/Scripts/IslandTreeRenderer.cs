using System.Collections.Generic;
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

        // Runtime draw data (rebuilt from the serialized species; not serialized itself).
        struct Batch
        {
            public Mesh mesh; public int submesh; public Material material;
            public Matrix4x4[] matrices; public Vector3[] centers; public float[] rand;
            public float radius; public float topOffset; public Bounds bounds; public Matrix4x4[] scratch;
        }
        readonly List<Batch> _batches = new List<Batch>();
        static readonly Plane[] _planes = new Plane[6];
        Terrain _terrain;

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

        public void Finish() => Rebuild();

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
        }

        // ---- trunk collider streaming ----

        void LateUpdate()
        {
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

                        var matrices = new Matrix4x4[n];
                        var centers = new Vector3[n];
                        var rand = new float[n];
                        var b = new Bounds(sp.positions[0], Vector3.zero);
                        for (int i = 0; i < n; i++)
                        {
                            matrices[i] = Matrix4x4.TRS(sp.positions[i], sp.rotations[i], sp.scales[i]) * partLocal;
                            centers[i] = sp.positions[i];
                            rand[i] = Hash01((uint)i * 2654435761u ^ (uint)s);
                            b.Encapsulate(sp.positions[i]);
                        }
                        b.Expand(radius * 2f);
                        _batches.Add(new Batch
                        {
                            mesh = mesh, submesh = s, material = mat,
                            matrices = matrices, centers = centers, rand = rand,
                            radius = radius, topOffset = topOffset, bounds = b, scratch = new Matrix4x4[n]
                        });
                    }
                }
            }
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (!_built) Rebuild();          // lazy build once serialized species are guaranteed present
            if (_batches.Count == 0) return;

            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            Vector3 camPos = cam.transform.position;
            var shadow = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            float near2 = lodNear * lodNear;
            float span = Mathf.Max(1f, lodFar - lodNear);
            if (_terrain == null) _terrain = GetComponent<Terrain>();
            bool doOcclusion = terrainOcclusion && _terrain != null;
            float occlMin2 = occlusionMinDistance * occlusionMinDistance;
            float terrainBaseY = _terrain != null ? _terrain.transform.position.y : 0f;

            for (int bi = 0; bi < _batches.Count; bi++)
            {
                var b = _batches[bi];
                if (b.mesh == null || b.material == null) continue;
                // Whole-batch reject if the island's tree cloud is entirely off-screen.
                if (!GeometryUtility.TestPlanesAABB(_planes, b.bounds)) continue;

                int vis = 0;
                for (int i = 0; i < b.matrices.Length; i++)
                {
                    Vector3 c = b.centers[i];
                    if (!InFrustum(c, b.radius)) continue;                       // per-instance frustum cull

                    float dx = c.x - camPos.x, dz = c.z - camPos.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > near2)                                              // distance density LOD
                    {
                        float t = Mathf.Clamp01((Mathf.Sqrt(d2) - lodNear) / span);
                        float keep = Mathf.Lerp(1f, lodMinKeep, t);
                        if (b.rand[i] > keep) continue;
                    }
                    // Terrain occlusion: skip trees the island's own hill hides (far slope behind the peak).
                    if (doOcclusion && d2 > occlMin2 && Occluded(camPos, c, b.topOffset, terrainBaseY)) continue;

                    b.scratch[vis++] = b.matrices[i];
                }
                if (vis == 0) continue;

                var rp = new RenderParams(b.material)
                {
                    camera = cam, worldBounds = b.bounds,
                    shadowCastingMode = shadow, receiveShadows = true,
                    layer = FoliageLayer >= 0 ? FoliageLayer : 0
                };
                for (int start = 0; start < vis; start += 1023)
                    Graphics.RenderMeshInstanced(rp, b.mesh, b.submesh, b.scratch, Mathf.Min(1023, vis - start), start);
            }
        }

        static bool InFrustum(Vector3 center, float radius)
        {
            for (int p = 0; p < 6; p++)
                if (_planes[p].GetDistanceToPoint(center) < -radius) return false;
            return true;
        }

        /// <summary>
        /// Cheap terrain occlusion: sample the island height at a few points between the camera and the tree.
        /// If a ridge rises above the straight line from the camera EYE to the tree's CANOPY top, the tree is
        /// hidden behind the hill and can be skipped. Aiming the sight-line at the canopy (base + topOffset)
        /// keeps trees whose crowns peek over the ridge. 3 samples ≈ enough for a single-hill island.
        /// </summary>
        bool Occluded(Vector3 cam, Vector3 tree, float canopy, float terrainBaseY)
        {
            float targetY = tree.y + canopy;
            // Sample nearer the tree first (that's where the blocking peak usually is on the far slope).
            for (float t = 0.75f; t > 0.25f; t -= 0.25f)   // 0.75, 0.50, 0.25
            {
                Vector3 s = Vector3.Lerp(cam, tree, t);
                float ground = _terrain.SampleHeight(s) + terrainBaseY;
                float sight = Mathf.Lerp(cam.y, targetY, t);
                if (ground > sight + occlusionBias) return true;
            }
            return false;
        }

        static float Hash01(uint x)
        {
            x ^= x >> 16; x *= 2246822519u; x ^= x >> 13; x *= 3266489917u; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777215.0f;
        }
    }
}
