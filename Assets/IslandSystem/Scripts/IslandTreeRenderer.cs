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
        public float lodNear = 160f;
        [Tooltip("At this distance only lodMinKeep of the trees remain.")]
        public float lodFar = 600f;
        [Tooltip("Fraction of trees kept at/after lodFar (0.2 = 20%).")]
        [Range(0.05f, 1f)] public float lodMinKeep = 0.2f;

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

        // ---- rendering ----

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            // Defer the (re)build to the first camera render. Building here in OnEnable can run before the
            // serialized `species` list is fully restored after a domain reload, leaving empty batches and
            // invisible trees in the editor; by the first beginCameraRendering the scene is fully deserialized.
            _built = false;
        }

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        void Rebuild()
        {
            _batches.Clear();
            _built = true;
            if (species == null) return;

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
