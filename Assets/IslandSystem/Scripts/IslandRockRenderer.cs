using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandSystem
{
    /// <summary>
    /// Draws an island's ROCKS with GPU instancing (Graphics.RenderMeshInstanced) — no per-rock GameObject and no
    /// colliders. Lives as a COMPONENT on the terrain GameObject (so there is no "Rocks" child cluttering the
    /// hierarchy). Per-instance frustum culling only — rocks are sparse, so no density LOD / terrain occlusion.
    /// Placement is filled by <see cref="IslandTerrainGenerator.ScatterRocks"/>; drawn on the Foliage layer so the
    /// water's planar reflection can exclude it. Serialized so it survives domain reloads / scene save.
    /// </summary>
    [ExecuteAlways]
    public class IslandRockRenderer : MonoBehaviour
    {
        [System.Serializable]
        public class Species
        {
            public GameObject prefab;
            public List<Vector3> positions = new List<Vector3>();
            public List<Quaternion> rotations = new List<Quaternion>();
            public List<Vector3> scales = new List<Vector3>();   // already folds the prefab-root scale × instance scale
        }

        [SerializeField] List<Species> species = new List<Species>();
        public bool castShadows = true;

        struct Batch
        {
            public Mesh mesh; public int submesh; public Material material;
            public Matrix4x4[] matrices; public Vector3[] centers; public float radius; public Bounds bounds; public Matrix4x4[] scratch;
        }
        readonly List<Batch> _batches = new List<Batch>();
        static readonly Plane[] _planes = new Plane[6];
        static int _foliageLayer = -2;
        static int FoliageLayer { get { if (_foliageLayer == -2) _foliageLayer = LayerMask.NameToLayer("Foliage"); return _foliageLayer; } }
        bool _built;

        // ---- build API (called by IslandTerrainGenerator.ScatterRocks) ----

        public void BeginBuild() { species.Clear(); _batches.Clear(); _built = false; }

        /// <summary>Record one rock instance. <paramref name="scale"/> must already fold in the prefab root scale.</summary>
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

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            _built = false;   // (re)build on the first camera render — serialized species are guaranteed present then
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
                    float maxScale = 0f; for (int i = 0; i < n; i++) maxScale = Mathf.Max(maxScale, sp.scales[i].x, sp.scales[i].y, sp.scales[i].z);
                    float radius = mesh.bounds.extents.magnitude * Mathf.Max(0.01f, maxScale) + 0.5f;

                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var mat = (sharedMats != null && sharedMats.Length > 0) ? sharedMats[Mathf.Min(s, sharedMats.Length - 1)] : null;
                        if (mat == null) continue;
                        mat.enableInstancing = true;

                        var matrices = new Matrix4x4[n];
                        var centers = new Vector3[n];
                        var b = new Bounds(sp.positions[0], Vector3.zero);
                        for (int i = 0; i < n; i++)
                        {
                            matrices[i] = Matrix4x4.TRS(sp.positions[i], sp.rotations[i], sp.scales[i]) * partLocal;
                            centers[i] = sp.positions[i];
                            b.Encapsulate(sp.positions[i]);
                        }
                        b.Expand(radius * 2f);
                        _batches.Add(new Batch
                        {
                            mesh = mesh, submesh = s, material = mat,
                            matrices = matrices, centers = centers, radius = radius, bounds = b, scratch = new Matrix4x4[n]
                        });
                    }
                }
            }
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (!_built) Rebuild();
            if (_batches.Count == 0) return;

            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            var shadow = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

            for (int bi = 0; bi < _batches.Count; bi++)
            {
                var b = _batches[bi];
                if (b.mesh == null || b.material == null) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, b.bounds)) continue;

                int vis = 0;
                for (int i = 0; i < b.matrices.Length; i++)
                    if (InFrustum(b.centers[i], b.radius)) b.scratch[vis++] = b.matrices[i];
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
    }
}
