using System.Collections.Generic;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// One GPU-instancing draw (Cyanilux style, INDIRECT): a prefab mesh + material + a StructuredBuffer of
    /// per-instance WORLD matrices. Each frame a compute shader frustum-culls the instances into
    /// <see cref="visibleBuffer"/> (Append), its counter is copied into <see cref="indirectBuffer"/>'s
    /// instanceCount, and the batch is drawn with Graphics.RenderMeshIndirect — the shader walks
    /// _VisibleIDs[SV_InstanceID] → _PerInstanceData. Call <see cref="Dispose"/> when the chunk is removed.
    /// </summary>
    public struct GrassBatch
    {
        public Mesh mesh;
        public Material material;
        public GraphicsBuffer buffer;          // StructuredBuffer<float4x4> _PerInstanceData (all instances)
        public GraphicsBuffer visibleBuffer;   // Append<uint> _VisibleIDs — ids surviving the frustum cull
        public GraphicsBuffer indirectBuffer;  // IndirectDrawIndexedArgs; instanceCount ← CopyCount(visibleBuffer)
        public MaterialPropertyBlock mpb;      // binds both StructuredBuffers for the RenderParams
        public int count;                      // TOTAL instance count (compute dispatch size)
        public Bounds bounds;

        public void Dispose()
        {
            if (buffer != null) { buffer.Release(); buffer = null; }
            if (visibleBuffer != null) { visibleBuffer.Release(); visibleBuffer = null; }
            if (indirectBuffer != null) { indirectBuffer.Release(); indirectBuffer = null; }
        }
    }

    /// <summary>
    /// Builds GPU-INSTANCING batches for a grass chunk (a UV sub-rect of a terrain), used by
    /// <see cref="IslandGrassField"/> which streams chunks around the camera and draws them with
    /// Graphics.RenderMeshInstanced. For each biome band × grass LAYER, scatters instance transforms
    /// (position on the terrain, random Y rotation — or slope-aligned for flat moss — and random scale) of
    /// the layer's PREFAB mesh. One material per layer (the <c>IslandSystem/Grass</c> instanced shader with
    /// the prefab's texture) drives wind + player flatten. Runtime-safe (pure math from the seed).
    /// </summary>
    public static class GrassGenerator
    {
        const string ShaderName = "IslandSystem/Grass";
        static Shader _shader;
        static Shader GrassShader => _shader != null ? _shader : (_shader = Shader.Find(ShaderName));

        static readonly Dictionary<GrassSettings, Material> _mats = new Dictionary<GrassSettings, Material>();
        static readonly Dictionary<GameObject, Mesh> _meshes = new Dictionary<GameObject, Mesh>();

        /// <summary>Drop cached materials/meshes (call when layer params may have changed, e.g. on field enable).</summary>
        public static void ClearCache() { _mats.Clear(); _meshes.Clear(); }

        public static bool HasAnyGrass(List<IslandBand> bands)
        {
            if (bands == null) return false;
            foreach (var b in bands)
            {
                if (b.biome == null || b.biome.grassLayers == null) continue;
                foreach (var l in b.biome.grassLayers) if (l != null && l.IsValid) return true;
            }
            return false;
        }

        public static List<GrassBatch> BuildChunkInstances(Terrain terrain, List<IslandBand> bands, float waterline,
            int seed, float uMin, float uMax, float vMin, float vMax)
        {
            var batches = new List<GrassBatch>();
            if (bands == null || GrassShader == null) return batches;
            var data = terrain.terrainData;
            Vector3 size = data.size;
            Vector3 origin = terrain.transform.position;

            int bandIdx = 0;
            foreach (var band in bands)
            {
                bandIdx++;
                var biome = band.biome;
                if (biome == null || biome.grassLayers == null) continue;

                int layerIdx = 0;
                foreach (var gs in biome.grassLayers)
                {
                    layerIdx++;
                    if (gs == null || !gs.IsValid) continue;
                    var mesh = GetPrefabMesh(gs.prefab);
                    if (mesh == null) continue;

                    int lseed = seed ^ (bandIdx * 92821) ^ (layerIdx * 40503);
                    var mats = Scatter(data, size, origin, band, gs, waterline, lseed, uMin, uMax, vMin, vMax);
                    if (mats.Count == 0) continue;

                    // StructuredBuffer<float4x4> of world matrices, indexed in the shader by SV_InstanceID.
                    // Matrix4x4 == 16 floats == 64 bytes; must match the shader's float4x4 stride.
                    var arr = mats.ToArray();
                    var buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, arr.Length, 64);
                    buf.SetData(arr);

                    // Append buffer of VISIBLE ids, refilled by the frustum-cull compute each camera. Pre-seeded
                    // with the identity mapping so the batch still draws fully if the compute shader is missing
                    // (the indirect args below then simply keep their initial instanceCount = all).
                    var visible = new GraphicsBuffer(GraphicsBuffer.Target.Append, arr.Length, sizeof(uint));
                    var ids = new uint[arr.Length];
                    for (uint k = 0; k < ids.Length; k++) ids[k] = k;
                    visible.SetData(ids);

                    // Indirect draw args (Cyanilux indirect setup): one command, startInstance stays 0 so the
                    // shader needs no UnityIndirect offset handling. instanceCount is overwritten per camera by
                    // GraphicsBuffer.CopyCount from the append buffer's hidden counter.
                    var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
                    args[0].indexCountPerInstance = mesh.GetIndexCount(0);
                    args[0].instanceCount = (uint)arr.Length;
                    args[0].startIndex = mesh.GetIndexStart(0);
                    args[0].baseVertexIndex = mesh.GetBaseVertex(0);
                    args[0].startInstance = 0;
                    var indirect = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    indirect.SetData(args);

                    var mpb = new MaterialPropertyBlock();
                    mpb.SetBuffer("_PerInstanceData", buf);
                    mpb.SetBuffer("_VisibleIDs", visible);

                    Vector3 c = origin + new Vector3((uMin + uMax) * 0.5f * size.x, size.y * 0.5f, (vMin + vMax) * 0.5f * size.z);
                    Vector3 e = new Vector3((uMax - uMin) * size.x * 0.5f + 3f, size.y * 0.5f + 3f, (vMax - vMin) * size.z * 0.5f + 3f);
                    batches.Add(new GrassBatch
                    {
                        mesh = mesh,
                        material = GetLayerMaterial(gs, mesh),
                        buffer = buf,
                        visibleBuffer = visible,
                        indirectBuffer = indirect,
                        mpb = mpb,
                        count = arr.Length,
                        bounds = new Bounds(c, e * 2f)
                    });
                }
            }
            return batches;
        }

        static List<Matrix4x4> Scatter(TerrainData data, Vector3 size, Vector3 origin, IslandBand band, GrassSettings gs,
            float waterline, int seed, float uMin, float uMax, float vMin, float vMax)
        {
            var rng = new System.Random(seed);
            float spacing = 10f / Mathf.Sqrt(Mathf.Max(0.01f, gs.density));
            int gx = Mathf.Max(1, Mathf.CeilToInt((uMax - uMin) * size.x / spacing));
            int gz = Mathf.Max(1, Mathf.CeilToInt((vMax - vMin) * size.z / spacing));
            var list = new List<Matrix4x4>();

            for (int iz = 0; iz < gz; iz++)
            {
                for (int ix = 0; ix < gx; ix++)
                {
                    float u = Mathf.Clamp01(Mathf.Lerp(uMin, uMax, (ix + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.85f) / gx));
                    float v = Mathf.Clamp01(Mathf.Lerp(vMin, vMax, (iz + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.85f) / gz));
                    float h01 = data.GetInterpolatedHeight(u, v) / Mathf.Max(0.0001f, size.y);
                    if (h01 <= waterline || h01 < band.lo || h01 > band.hi) continue;
                    float slope = data.GetSteepness(u, v);
                    if ((float)rng.NextDouble() > IslandTerrainGenerator.ConditionWeight(gs.where, h01, slope)) continue;

                    Vector3 wp = origin + new Vector3(u * size.x, data.GetInterpolatedHeight(u, v), v * size.z);
                    float sc = Mathf.Lerp(gs.prefabScaleRange.x, gs.prefabScaleRange.y, (float)rng.NextDouble());
                    float yaw = (float)rng.NextDouble() * 360f;
                    Quaternion rot = gs.layFlat
                        ? Quaternion.FromToRotation(Vector3.up, data.GetInterpolatedNormal(u, v)) * Quaternion.Euler(0f, yaw, 0f)
                        : Quaternion.Euler(0f, yaw, 0f);
                    list.Add(Matrix4x4.TRS(wp, rot, Vector3.one * sc));
                }
            }
            return list;
        }

        static Material GetLayerMaterial(GrassSettings gs, Mesh mesh)
        {
            if (_mats.TryGetValue(gs, out var cached) && cached != null) return cached;

            var m = new Material(GrassShader) { name = $"GrassMat_{gs.name}", enableInstancing = true };
            Texture tex = null;
            var r = gs.prefab.GetComponentInChildren<Renderer>();
            var pm = r != null ? r.sharedMaterial : null;
            if (pm != null)
            {
                if (pm.HasProperty("_BaseMap") && pm.GetTexture("_BaseMap") != null) tex = pm.GetTexture("_BaseMap");
                else if (pm.HasProperty("_MainTex") && pm.GetTexture("_MainTex") != null) tex = pm.GetTexture("_MainTex");
            }
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetFloat("_Cutoff", gs.alphaCutoff);
            m.SetColor("_BottomColor", gs.bottomColor);   // white/white = plain texture colour
            m.SetColor("_TopColor", gs.topColor);
            m.SetFloat("_Tiles", Mathf.Max(1, gs.textureTiles));
            // Object-space blade height (the shader bends by positionOS.y / _WindHeight, so NO instance scale here).
            m.SetFloat("_WindHeight", Mathf.Max(0.05f, mesh.bounds.size.y));
            m.SetFloat("_WindStrength", gs.windStrength);
            m.SetFloat("_WindSpeed", gs.windSpeed);
            m.SetFloat("_WindScale", gs.windScale);
            m.SetFloat("_BendStrength", gs.bendStrength);
            m.SetFloat("_AmbientBoost", 0.18f); // gentle ambient so grass isn't washed-out/neon
            _mats[gs] = m;
            return m;
        }

        static Mesh GetPrefabMesh(GameObject prefab)
        {
            if (_meshes.TryGetValue(prefab, out var cached)) return cached;
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var src = mf != null ? mf.sharedMesh : null;
            if (src == null) { _meshes[prefab] = null; return null; }

            // Bake the mesh's transform WITHIN the prefab (incl. the FBX import scale, e.g. ×100, and any child
            // offset/rotation) into a copy, so the instance renders at its natural size regardless of the prefab
            // hierarchy. Instances then just use TRS(worldPos, yaw, userScale).
            Matrix4x4 local = mf.transform.localToWorldMatrix;
            Mesh mesh;
            if (local.isIdentity) mesh = src;
            else
            {
                mesh = Object.Instantiate(src);
                mesh.name = src.name + "_baked";
                var vs = mesh.vertices;
                for (int i = 0; i < vs.Length; i++) vs[i] = local.MultiplyPoint3x4(vs[i]);
                mesh.vertices = vs;
                var ns = mesh.normals;
                if (ns != null && ns.Length == vs.Length)
                {
                    for (int i = 0; i < ns.Length; i++) ns[i] = local.MultiplyVector(ns[i]).normalized;
                    mesh.normals = ns;
                }
                mesh.RecalculateBounds();
            }
            _meshes[prefab] = mesh;
            return mesh;
        }
    }
}
