using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandSystem
{
    /// <summary>
    /// Modern GPU-instanced grass field (Cyanilux style, full INDIRECT setup). Splits an island's grass into
    /// CHUNKS around the viewer (player camera in play mode, Scene camera in the editor). Per camera, each
    /// chunk-layer batch is GPU-frustum-culled by a compute shader (visible ids → Append buffer, counter →
    /// indirect args instanceCount via CopyCount) and drawn with Graphics.RenderMeshIndirect — the per-instance
    /// world matrices live in a StructuredBuffer indexed through _VisibleIDs[SV_InstanceID] (no 1023-per-call
    /// limit), no baked meshes, no per-blade GameObjects. Only chunks within
    /// <see cref="viewDistance"/> exist, so dense grass stays cheap on a huge island. Reads the biome bands from
    /// the island's <see cref="IslandMarker"/>; per-chunk deterministic seed → same grass on every networked peer.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(IslandMarker))]
    public class IslandGrassField : MonoBehaviour
    {
        public Terrain terrain;
        [Tooltip("Explicit viewer to stream around (e.g. the player). Empty = Camera.main (play) / Scene camera (edit).")]
        public Transform viewer;

        /// <summary>
        /// The LOCAL player binding, set by PlayerEnvironment when the owned player spawns (networked prefabs
        /// spawn at runtime, so fields can't reference them in the scene). All fields stream around this
        /// transform and frustum-gate against this camera. Cleared on despawn.
        /// </summary>
        public static Transform LocalViewer;
        public static Camera LocalViewerCamera;
        public int seed;
        [Tooltip("Normalized waterline — grass only grows above it.")]
        public float waterline = 0.03f;
        [Tooltip("World size of a grass chunk (metres).")]
        public float chunkSize = 18f;
        [Tooltip("Grass exists within this radius of the viewer (metres). The shader fades blades out just " +
                 "before this so the field dissolves smoothly instead of ending in a hard square.")]
        public float viewDistance = 90f;
        [Tooltip("Max chunks built per update, so streaming in doesn't hitch.")]
        public int buildsPerTick = 4;

        readonly Dictionary<long, List<GrassBatch>> _chunks = new Dictionary<long, List<GrassBatch>>();
        List<IslandBand> _bands;
        int _builtThisTick;
        int _chunksVersion = -1;   // GrassGenerator.CacheVersion the current chunks were built with
        // Foliage layer (excluded from the water reflection). Resolved lazily — LayerMask.NameToLayer can't run
        // in a static field initializer (it fires during MonoBehaviour deserialization, which Unity forbids).
        static int _foliageLayer = -2;
        static int FoliageLayer { get { if (_foliageLayer == -2) _foliageLayer = LayerMask.NameToLayer("Foliage"); return _foliageLayer; } }

        // ---- GPU frustum culling (GrassFrustumCull.compute, loaded from Resources — runtime/multiplayer-safe) ----
        static ComputeShader _cullCS;
        static bool _cullSearched;
        static ComputeShader CullShader
        {
            get
            {
                if (_cullCS == null && !_cullSearched)
                {
                    _cullCS = Resources.Load<ComputeShader>("GrassFrustumCull");
                    _cullSearched = true;
                    if (_cullCS == null)
                        Debug.LogWarning("[Grass] GrassFrustumCull.compute not found in Resources — drawing without GPU frustum culling.");
                }
                return _cullCS;
            }
        }
        static readonly int PropPerInstance   = Shader.PropertyToID("_PerInstanceData");
        static readonly int PropVisibleAppend = Shader.PropertyToID("_VisibleIDsAppend");
        static readonly int PropMatrix        = Shader.PropertyToID("_Matrix");
        static readonly int PropMaxDist       = Shader.PropertyToID("_MaxDrawDistance");
        static readonly int PropCount         = Shader.PropertyToID("_InstanceCount");
        static readonly int PropFadeStart     = Shader.PropertyToID("_FadeStart");
        static readonly int PropFadeEnd       = Shader.PropertyToID("_FadeEnd");

        List<IslandBand> Bands
        {
            get { if (_bands == null) { var m = GetComponent<IslandMarker>(); _bands = m != null ? m.bands : null; } return _bands; }
        }

        void OnEnable()
        {
            GrassGenerator.ClearCache();
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
            DisposeAll();
        }

        void DisposeAll()
        {
            foreach (var kv in _chunks) DisposeChunk(kv.Value);
            _chunks.Clear();
        }

        static void DisposeChunk(List<GrassBatch> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) list[i].Dispose();
        }

        void LateUpdate() { if (Application.isPlaying) Stream(ViewerPos(), ViewerCamera()); }

#if UNITY_EDITOR
        void EditorTick() { if (this == null || Application.isPlaying) return; Stream(ViewerPos(), ViewerCamera()); }
#endif

        Vector3 ViewerPos()
        {
            if (viewer != null) return viewer.position;
            if (Application.isPlaying && LocalViewer != null) return LocalViewer.position;
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

        /// <summary>The camera whose FRUSTUM gates which chunks get built (grass only builds where you look).</summary>
        Camera ViewerCamera()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null) return sv.camera;
            }
#endif
            if (LocalViewerCamera != null) return LocalViewerCamera;
            return Camera.main;
        }

        /// <summary>Force streaming around an explicit viewer position (tests / cutscenes). Builds a full disk.</summary>
        public void RefreshAround(Vector3 v) { Stream(v, null); }

        // ---- Draw: GPU frustum cull + RenderMeshIndirect for each chunk-layer, per camera (edit + play) ----
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (_chunks.Count == 0) return;

            var cs = CullShader;
            Matrix4x4 vp = default;
            if (cs != null)
            {
                // Raw matrices (NOT GL.GetGPUProjectionMatrix) — the compute expects the OpenGL clip-space
                // convention Unity uses by default. Instance matrices are world-space, so VP is enough (no M).
                vp = cam.projectionMatrix * cam.worldToCameraMatrix;
            }
            // Generous cap: blades VANISH via the per-instance distance fade in the vertex shader, not a hard
            // cut here — a tight 3D-distance cap drew a visible arc when the camera flew above the field.
            float maxDist = viewDistance * 4f;

            foreach (var kv in _chunks)
            {
                var list = kv.Value; if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    if (b.count <= 0 || b.buffer == null || b.indirectBuffer == null || b.material == null || b.mesh == null) continue;

                    if (cs != null && b.visibleBuffer != null)
                    {
                        // Reset the append buffer's hidden counter, cull, then copy the counter into the
                        // indirect args' instanceCount (2nd uint → byte offset sizeof(uint)). All on the GPU.
                        b.visibleBuffer.SetCounterValue(0);
                        cs.SetBuffer(0, PropPerInstance, b.buffer);
                        cs.SetBuffer(0, PropVisibleAppend, b.visibleBuffer);
                        cs.SetMatrix(PropMatrix, vp);
                        cs.SetFloat(PropMaxDist, maxDist);
                        cs.SetInt(PropCount, b.count);
                        cs.Dispatch(0, (b.count + 63) / 64, 1, 1);
                        GraphicsBuffer.CopyCount(b.visibleBuffer, b.indirectBuffer, sizeof(uint));
                    }
                    // else: visibleBuffer is pre-seeded with 0..count-1 and the args keep instanceCount = count,
                    // so the indirect draw below still renders everything (graceful no-compute fallback).

                    var rp = new RenderParams(b.material)
                    {
                        camera = cam,
                        worldBounds = b.bounds,
                        matProps = b.mpb,                 // binds _PerInstanceData + _VisibleIDs
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = true,
                        layer = FoliageLayer >= 0 ? FoliageLayer : 0   // excluded from the water reflection
                    };
                    Graphics.RenderMeshIndirect(rp, b.mesh, b.indirectBuffer, 1);
                }
            }
        }

        void Stream(Vector3 viewerPos, Camera cam)
        {
            if (terrain == null || Bands == null || Bands.Count == 0) return;

            // Settings changed somewhere (GrassGenerator.ClearCache bumped the version): flush ALL chunks so
            // nothing keeps a stale material — otherwise old and new chunks sit side by side with different
            // colours and a hard seam shows along the chunk grid. Rebuilt over the next ticks.
            if (_chunksVersion != GrassGenerator.CacheVersion)
            {
                DisposeAll();
                _chunksVersion = GrassGenerator.CacheVersion;
            }

            _builtThisTick = 0;

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            int cvx = Mathf.FloorToInt((viewerPos.x - origin.x) / chunkSize);
            int cvz = Mathf.FloorToInt((viewerPos.z - origin.z) / chunkSize);
            int r = Mathf.CeilToInt(viewDistance / chunkSize);
            float keepR2 = (viewDistance + chunkSize) * (viewDistance + chunkSize);
            float innerR2 = (2f * chunkSize) * (2f * chunkSize);

            // Camera frustum planes (slightly padded): chunks are only BUILT where the player is looking, so
            // the budget goes into a deep wedge along the view instead of a disk wasted behind the back.
            // Already-built chunks are KEPT by distance (no rebuild churn when spinning the camera).
            Plane[] planes = null;
            if (cam != null)
            {
                planes = GeometryUtility.CalculateFrustumPlanes(cam);
                for (int i = 0; i < planes.Length; i++)
                {
                    var pl = planes[i];
                    pl.distance += chunkSize * 0.75f;   // pad so turning shows less pop-in at the view edges
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
                    want.Add(key);                        // kept by DISTANCE, independent of look direction
                    if (_chunks.ContainsKey(key)) continue;
                    if (_builtThisTick >= Mathf.Max(1, buildsPerTick)) continue;

                    // Frustum gate on BUILDING: skip chunks the camera can't see. The immediate ring around
                    // the viewer always builds (looking down / spawn / spinning in place).
                    if (planes != null && d2 > innerR2)
                    {
                        var bb = new Bounds(
                            new Vector3(wcx, origin.y + size.y * 0.5f, wcz),
                            new Vector3(chunkSize, size.y + 6f, chunkSize));
                        if (!GeometryUtility.TestPlanesAABB(planes, bb)) continue;
                    }

                    int cseed = seed ^ (cx * 73856093) ^ (cz * 19349663);
                    var batches = GrassGenerator.BuildChunkInstances(terrain, Bands, waterline, cseed,
                        Mathf.Clamp01(u0), Mathf.Clamp01(u1), Mathf.Clamp01(v0), Mathf.Clamp01(v1));
                    // Distance-fade band: each blade picks a random vanish distance inside it (dithered thinning,
                    // no contour arcs) — fully gone just inside viewDistance so the chunk boundary never shows.
                    for (int i = 0; i < batches.Count; i++)
                    {
                        batches[i].mpb.SetFloat(PropFadeStart, viewDistance * 0.55f);
                        batches[i].mpb.SetFloat(PropFadeEnd, viewDistance * 0.92f);
                    }
                    _chunks[key] = batches;           // may be empty; keep the key so we don't rebuild every tick
                    _builtThisTick++;
                }
            }

            if (_chunks.Count > want.Count)
            {
                var stale = new List<long>();
                foreach (var kv in _chunks) if (!want.Contains(kv.Key)) stale.Add(kv.Key);
                foreach (var k in stale) { DisposeChunk(_chunks[k]); _chunks.Remove(k); }
            }
        }

        static long Key(int cx, int cz) => ((long)(cx + 100000) << 21) ^ (uint)(cz + 100000);
    }
}
