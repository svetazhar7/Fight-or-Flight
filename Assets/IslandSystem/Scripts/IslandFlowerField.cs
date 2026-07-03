using System.Collections.Generic;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Streams FLOWER clusters around the viewer exactly like the grass (<see cref="IslandGrassField"/>):
    /// chunks within <see cref="viewDistance"/> are BUILT only when the camera looks at them (frustum gate,
    /// inner ring always) and removed by distance. Unlike the GPU-instanced grass, flowers are full prefab
    /// GameObjects — they keep their own materials and look — but they're sparse enough that building a
    /// chunk's worth is cheap. Per-chunk deterministic seed → identical flowers on every networked peer.
    /// Trees/rocks stay generation-time (always loaded); only flowers stream.
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
        public float chunkSize = 36f;
        [Tooltip("Flowers exist within this radius of the viewer (m).")]
        public float viewDistance = 150f;
        [Tooltip("Max chunks built per update, so streaming in doesn't hitch.")]
        public int buildsPerTick = 2;

        /// <summary>Same low-altitude gate the generation-time scatters use (no flowers on the sea fade).</summary>
        const float FadeThreshold = 0.05f;

        readonly Dictionary<long, GameObject> _chunks = new Dictionary<long, GameObject>();
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
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.update += EditorTick;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            ClearAll();
        }

        void ClearAll()
        {
            foreach (var kv in _chunks) DestroyChunk(kv.Value);
            _chunks.Clear();
        }

        static void DestroyChunk(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
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
                foreach (var k in stale) { DestroyChunk(_chunks[k]); _chunks.Remove(k); }
            }
        }

        /// <summary>
        /// Builds one chunk's flower clusters (may return null if nothing spawned): per band × rule, a
        /// jittered grid of cluster centres (density = CLUSTERS per 100 m²) gated by band / condition /
        /// village; every accepted centre spawns clusterSize flowers inside clusterRadius, each re-gated
        /// individually so patches trim at band edges and village rims.
        /// </summary>
        GameObject BuildChunk(int cx, int cz, int cseed, Vector3 origin, Vector3 size,
            float uMin, float uMax, float vMin, float vMax)
        {
            var marker = Marker;
            var data = terrain.terrainData;
            GameObject holder = null;

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

                                if (holder == null)
                                {
                                    holder = new GameObject($"FlowerChunk_{cx}_{cz}");
                                    // transient: never serialized into the scene, exactly like grass chunks
                                    holder.hideFlags = HideFlags.DontSave;
                                    holder.transform.SetParent(terrain.transform, false);
                                }

                                Vector3 worldPos = origin + new Vector3(wx, data.GetInterpolatedHeight(fu, fv) - rule.sink, wz);
                                GameObject go = Instantiate(prefab, holder.transform);
                                go.hideFlags = HideFlags.DontSave;
                                go.transform.position = worldPos;

                                Quaternion rot = rule.alignToNormal
                                    ? Quaternion.FromToRotation(Vector3.up, data.GetInterpolatedNormal(fu, fv))
                                    : Quaternion.identity;
                                if (rule.randomYRotation)
                                    rot = rot * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                                go.transform.rotation = rot;
                                go.transform.localScale = go.transform.localScale * s;
                            }
                        }
                    }
                }
            }
            return holder;
        }

        static long Key(int cx, int cz) => ((long)(cx + 100000) << 21) ^ (uint)(cz + 100000);
    }
}
