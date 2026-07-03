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

        /// <summary>
        /// Bumped by <see cref="ClearCache"/>. <see cref="IslandGrassField"/> compares it against the version its
        /// chunks were built with and flushes STALE chunks — otherwise chunks built before a settings change keep
        /// the old cached material and a hard seam appears along the chunk grid next to freshly built ones.
        /// </summary>
        public static int CacheVersion { get; private set; }

        /// <summary>Drop cached materials/meshes (call when layer params may have changed, e.g. on field enable).</summary>
        public static void ClearCache() { _mats.Clear(); _meshes.Clear(); CacheVersion++; }

        public static bool HasAnyGrass(List<IslandBand> bands)
        {
            if (bands == null) return false;
            foreach (var b in bands) if (BiomeHasGrass(b.biome)) return true;
            return false;
        }

        static bool BiomeHasGrass(BiomeAssetManifest biome)
        {
            if (biome == null || biome.grassLayers == null) return false;
            foreach (var l in biome.grassLayers) if (l != null && l.IsValid) return true;
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

                // Cross-biome blending only makes sense into a neighbour that ALSO grows grass. If the band
                // next door is grassless (beach sand, bare mountains), this band's grass must NOT spill over —
                // it thins out INSIDE its own edge instead.
                bool blendLo = false, blendHi = false;
                foreach (var other in bands)
                {
                    if (other.lo == band.lo && other.hi == band.hi) continue; // itself
                    if (Mathf.Abs(other.hi - band.lo) < 0.005f) blendLo |= BiomeHasGrass(other.biome);
                    if (Mathf.Abs(other.lo - band.hi) < 0.005f) blendHi |= BiomeHasGrass(other.biome);
                }

                int layerIdx = 0;
                foreach (var gs in biome.grassLayers)
                {
                    layerIdx++;
                    if (gs == null || !gs.IsValid) continue;
                    var mesh = GetPrefabMesh(gs.prefab);
                    if (mesh == null) continue;

                    int lseed = seed ^ (bandIdx * 92821) ^ (layerIdx * 40503);
                    var mats = Scatter(data, size, origin, band, gs, waterline, lseed, uMin, uMax, vMin, vMax, blendLo, blendHi);
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

        /// <summary>Half-width of the biome grass blend zone (normalized height). Inside it a layer's grass
        /// DITHERS out while the neighbouring band's grass dithers in — smooth biome-to-biome handoff.</summary>
        const float EdgeBlend = 0.02f;
        /// <summary>Wobble amplitude of the band boundary (normalized height), so the transition wanders
        /// organically instead of following a clean height isoline.</summary>
        const float EdgeNoise = 0.012f;

        // Deterministic 2D value noise (world-space) — identical on every networked peer, and identical for the
        // two bands sharing an edge, so their dither probabilities stay complementary (combined density ~constant).
        static float Hash2 (int x, int z)
        {
            unchecked
            {
                int h = x * 374761393 + z * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7FFFFFFF) / (float)int.MaxValue;
            }
        }
        static float ValueNoise (float x, float z)
        {
            int x0 = Mathf.FloorToInt(x), z0 = Mathf.FloorToInt(z);
            float fx = x - x0, fz = z - z0;
            fx = fx * fx * (3f - 2f * fx); fz = fz * fz * (3f - 2f * fz);
            float a = Hash2(x0, z0), b = Hash2(x0 + 1, z0), c = Hash2(x0, z0 + 1), d = Hash2(x0 + 1, z0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        static List<Matrix4x4> Scatter(TerrainData data, Vector3 size, Vector3 origin, IslandBand band, GrassSettings gs,
            float waterline, int seed, float uMin, float uMax, float vMin, float vMax, bool blendLo, bool blendHi)
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
                    if (h01 <= waterline) continue;   // hard waterline — no grass under the sea

                    // SOFT band edges: wobble the boundary with world noise, then dither this layer's grass out
                    // across the blend zone. Against a GRASSY neighbour the zone straddles the boundary (the
                    // neighbour mirrors it with the SAME noise → the two biomes' grass interleaves). Against a
                    // GRASSLESS neighbour (sand, rock) the grass never crosses the line — it thins out inside.
                    float wobble = (ValueNoise((origin.x + u * size.x) * 0.045f, (origin.z + v * size.z) * 0.045f) * 2f - 1f) * EdgeNoise;
                    float hb = h01 + wobble;

                    float wLo;
                    if (band.lo <= 0.001f) wLo = 1f;
                    else if (blendLo) wLo = Mathf.Clamp01((hb - (band.lo - EdgeBlend)) / (2f * EdgeBlend));
                    else
                    {
                        if (h01 < band.lo) continue;                                     // hard edge: stay off the sand
                        wLo = Mathf.Clamp01((hb - band.lo) / (2f * EdgeBlend));          // thin toward it, inside own band
                    }

                    float wHi;
                    if (band.hi >= 0.999f) wHi = 1f;
                    else if (blendHi) wHi = Mathf.Clamp01(((band.hi + EdgeBlend) - hb) / (2f * EdgeBlend));
                    else
                    {
                        if (h01 > band.hi) continue;
                        wHi = Mathf.Clamp01((band.hi - hb) / (2f * EdgeBlend));
                    }

                    float bandW = Mathf.Min(wLo, wHi);
                    if (bandW <= 0f) continue;
                    if (bandW < 1f && (float)rng.NextDouble() > bandW) continue;

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
            m.SetColor("_DryBottomColor", gs.dryBottomColor);
            m.SetColor("_DryTopColor", gs.dryTopColor);
            m.SetFloat("_ColorVariation", gs.colorVariation);
            m.SetFloat("_VariationScale", gs.variationScale);
            // Object-space blade height (the shader bends by positionOS.y / _WindHeight, so NO instance scale here).
            m.SetFloat("_WindHeight", Mathf.Max(0.05f, mesh.bounds.size.y));
            m.SetFloat("_WindStrength", gs.windStrength);
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
