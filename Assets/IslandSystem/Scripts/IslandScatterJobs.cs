using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace IslandSystem
{
    /// <summary>
    /// DOTS (Job System + Burst) placement of island objects — trees, rocks/props and flowers. The heavy
    /// per-candidate work (heightmap sampling, slope/normal, condition weight, RNG, Poisson spacing, cluster
    /// spawning) runs in Burst-compiled jobs over a <see cref="HeightField"/> built from the terrain heightmap,
    /// instead of thousands of managed <c>TerrainData.GetInterpolatedHeight/GetSteepness</c> calls. Each job
    /// only OUTPUTS blittable <see cref="ScatterInstance"/> transforms; the managed side reads them back and
    /// registers the GPU-instanced trees / instantiates the rock GameObjects / builds the flower matrices.
    ///
    /// Because Burst can't call <c>Mathf.PerlinNoise</c> or <c>System.Random</c>, placement uses
    /// <see cref="Unity.Mathematics.Random"/> and array-sampled height/slope — so object positions differ from
    /// the old CPU path, but every peer runs the SAME jobs from the SAME seed, so the world stays deterministic
    /// and multiplayer-consistent.
    /// </summary>
    public struct HeightField
    {
        [ReadOnly] public NativeArray<float> h;   // row-major [y*res + x], normalized 0..1 (as TerrainData stores)
        public int res;
        public float sizeX, sizeY, sizeZ;
        public float3 origin;

        public bool IsCreated => h.IsCreated;

        /// <summary>Copies a terrain's heightmap into a native array (managed — call off the job thread).</summary>
        public static HeightField FromTerrain(Terrain terrain, Allocator alloc)
        {
            var data = terrain.terrainData;
            int res = data.heightmapResolution;
            float[,] managed = data.GetHeights(0, 0, res, res);   // [y, x]
            var na = new NativeArray<float>(res * res, alloc, NativeArrayOptions.UninitializedMemory);
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    na[y * res + x] = managed[y, x];
            Vector3 s = data.size;
            return new HeightField
            {
                h = na, res = res,
                sizeX = s.x, sizeY = s.y, sizeZ = s.z,
                origin = terrain.transform.position
            };
        }

        public void Dispose() { if (h.IsCreated) h.Dispose(); }

        /// <summary>Bilinear normalized height (0..1) at UV, matching TerrainData.GetInterpolatedHeight/size.y.</summary>
        public float H01(float u, float v)
        {
            float fx = math.saturate(u) * (res - 1);
            float fy = math.saturate(v) * (res - 1);
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = math.min(x0 + 1, res - 1), y1 = math.min(y0 + 1, res - 1);
            float tx = fx - x0, ty = fy - y0;
            float a = h[y0 * res + x0], b = h[y0 * res + x1];
            float c = h[y1 * res + x0], d = h[y1 * res + x1];
            return math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), ty);
        }

        public float HWorld(float u, float v) => H01(u, v) * sizeY;

        /// <summary>Slope in degrees from the central-difference gradient (same formula as the CPU SlopeAt).</summary>
        public float SlopeDeg(float u, float v)
        {
            int x = math.clamp((int)math.round(u * (res - 1)), 1, res - 2);
            int y = math.clamp((int)math.round(v * (res - 1)), 1, res - 2);
            float dhx = (h[y * res + x + 1] - h[y * res + x - 1]) * sizeY / (2f * (sizeX / (res - 1)));
            float dhz = (h[(y + 1) * res + x] - h[(y - 1) * res + x]) * sizeY / (2f * (sizeZ / (res - 1)));
            return math.atan(math.sqrt(dhx * dhx + dhz * dhz)) * math.TODEGREES;
        }

        /// <summary>Ground normal (world) from the central-difference gradient.</summary>
        public float3 Normal(float u, float v)
        {
            int x = math.clamp((int)math.round(u * (res - 1)), 1, res - 2);
            int y = math.clamp((int)math.round(v * (res - 1)), 1, res - 2);
            float dhx = (h[y * res + x + 1] - h[y * res + x - 1]) * sizeY / (2f * (sizeX / (res - 1)));
            float dhz = (h[(y + 1) * res + x] - h[(y - 1) * res + x]) * sizeY / (2f * (sizeZ / (res - 1)));
            return math.normalize(new float3(-dhx, 1f, -dhz));
        }
    }

    /// <summary>Blittable mirror of a spawn rule for the jobs (indices point into flattened prefab/weight arrays).</summary>
    public struct ScatterRuleB
    {
        public PlacementCondition where;   // blittable (6 floats)
        public float bandLo, bandHi;
        public float density, spacingVariation;   // trees / flowers
        public int count;                          // rocks
        public float2 scaleRange;
        public float sink;
        public byte alignToNormal, randomYRotation, nonUniformScale;
        public int prefabStart, prefabCount;   // slice into the flattened prefab list
        public int weightStart, weightCount;   // slice into the flattened weight list
        public int2 clusterSize;               // flowers: [min,max] inclusive
        public float clusterRadius;            // flowers
        public uint seed;                      // per-rule (or per-chunk) RNG seed
    }

    /// <summary>One placed object the managed side turns into an instanced draw / GameObject.</summary>
    public struct ScatterInstance
    {
        public float3 pos;        // world position
        public quaternion rot;    // final rotation
        public float3 scale;      // multiplier to fold onto the prefab's own scale
        public int prefab;        // index into the flattened prefab list
    }

    /// <summary>Burst-compatible placement helpers shared by every scatter job.</summary>
    public static class ScatterMath
    {
        public const float FadeThreshold = 0.05f;

        static float Band(float v, float min, float max, float blend)
        {
            float rising = math.smoothstep(0f, 1f, (v - min) / blend);
            float falling = math.smoothstep(0f, 1f, (max - v) / blend);
            return math.saturate(math.min(rising, falling));
        }

        public static float ConditionWeight(in PlacementCondition c, float h01, float slopeDeg)
        {
            float hw = (c.maxHeight <= c.minHeight + 1e-4f) ? 1f : Band(h01, c.minHeight, c.maxHeight, math.max(1e-4f, c.heightBlend));
            float sw = (c.maxSlope <= c.minSlope + 1e-4f) ? 1f : Band(slopeDeg, c.minSlope, c.maxSlope, math.max(1e-4f, c.slopeBlend));
            return hw * sw;
        }

        /// <summary>villages: xy = centre UV, z = flat-zone radius (world units).</summary>
        public static bool InAnyVillage(float u, float v, float sizeX, float sizeZ, in NativeArray<float3> villages)
        {
            float lx = u * sizeX, lz = v * sizeZ;
            for (int i = 0; i < villages.Length; i++)
            {
                float3 z = villages[i];
                float dx = lx - z.x * sizeX, dz = lz - z.y * sizeZ;
                if (dx * dx + dz * dz < z.z * z.z) return true;
            }
            return false;
        }

        public static int PickWeighted(ref Random rng, in NativeArray<float> weights, int wStart, int wCount, int prefabCount)
        {
            if (prefabCount <= 1) return 0;
            if (wCount == 0) return rng.NextInt(prefabCount);
            float total = 0f;
            for (int i = 0; i < prefabCount; i++) total += (i < wCount ? math.max(0f, weights[wStart + i]) : 0f);
            if (total <= 1e-4f) return rng.NextInt(prefabCount);
            float r = rng.NextFloat() * total, acc = 0f;
            for (int i = 0; i < prefabCount; i++)
            {
                acc += (i < wCount ? math.max(0f, weights[wStart + i]) : 0f);
                if (r <= acc) return i;
            }
            return prefabCount - 1;
        }

        /// <summary>Rotation that maps +Y onto the ground normal (Burst FromToRotation(up, n)).</summary>
        public static quaternion AlignUp(float3 n)
        {
            n = math.normalizesafe(n, math.up());
            float3 up = math.up();
            float d = math.dot(up, n);
            if (d >= 0.99999f) return quaternion.identity;
            if (d <= -0.99999f) return quaternion.AxisAngle(math.right(), math.PI);
            float3 axis = math.normalize(math.cross(up, n));
            return quaternion.AxisAngle(axis, math.acos(math.clamp(d, -1f, 1f)));
        }
    }

    /// <summary>
    /// Density-based Poisson-disc tree scatter (single-threaded — the no-overlap test reads previously placed
    /// trees). Mirrors the old PlaceTrees dart-throwing but Burst-compiled over a <see cref="HeightField"/>.
    /// </summary>
    [BurstCompile]
    public struct TreeScatterJob : IJob
    {
        public HeightField hf;
        [ReadOnly] public NativeArray<ScatterRuleB> rules;
        [ReadOnly] public NativeArray<float> weights;
        [ReadOnly] public NativeArray<float> prefabRadius;   // per flattened prefab
        [ReadOnly] public NativeArray<float3> villages;
        public float cell;
        public float pack;
        public NativeParallelMultiHashMap<int2, float3> occ;   // cell -> (worldX, worldZ, radius)
        public NativeList<ScatterInstance> outList;

        public void Execute()
        {
            for (int ri = 0; ri < rules.Length; ri++)
            {
                ScatterRuleB rule = rules[ri];
                if (rule.prefabCount <= 0) continue;
                float density = rule.density > 0f ? rule.density : 2f;
                float spacing = 10f / math.sqrt(density);
                float variation = math.saturate(rule.spacingVariation);
                int target = (int)math.ceil(hf.sizeX * hf.sizeZ / (spacing * spacing));
                int attempts = math.clamp(target * 5, 64, 4_000_000);
                var rng = new Random(rule.seed == 0u ? 1u : rule.seed);

                for (int a = 0; a < attempts; a++)
                {
                    float u = rng.NextFloat(), v = rng.NextFloat();
                    if (ScatterMath.InAnyVillage(u, v, hf.sizeX, hf.sizeZ, villages)) continue;
                    float h01 = hf.H01(u, v);
                    if (h01 < ScatterMath.FadeThreshold || h01 < rule.bandLo || h01 > rule.bandHi) continue;
                    float slope = hf.SlopeDeg(u, v);
                    if (rng.NextFloat() > ScatterMath.ConditionWeight(rule.where, h01, slope)) continue;

                    float wx = u * hf.sizeX, wz = v * hf.sizeZ;
                    float sep = spacing * math.lerp(1f - 0.5f * variation, 1f + 0.5f * variation, rng.NextFloat());
                    if (TooClose(wx, wz, sep * 0.5f)) continue;

                    int local = ScatterMath.PickWeighted(ref rng, weights, rule.weightStart, rule.weightCount, rule.prefabCount);
                    int global = rule.prefabStart + local;
                    float s = math.lerp(rule.scaleRange.x, rule.scaleRange.y, rng.NextFloat());
                    if (s <= 0f) s = 1f;
                    float pr = prefabRadius[global];
                    Register(wx, wz, math.max(sep * 0.5f, pr * s));

                    float3 pos = hf.origin + new float3(wx, hf.HWorld(u, v) - rule.sink, wz);
                    quaternion rot = rule.alignToNormal != 0 ? ScatterMath.AlignUp(hf.Normal(u, v)) : quaternion.identity;
                    if (rule.randomYRotation != 0) rot = math.mul(rot, quaternion.RotateY(rng.NextFloat() * 2f * math.PI));
                    outList.Add(new ScatterInstance { pos = pos, rot = rot, scale = new float3(s), prefab = global });
                }
            }
        }

        bool TooClose(float wx, float wz, float r)
        {
            int cx = (int)math.floor(wx / cell), cz = (int)math.floor(wz / cell);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var key = new int2(cx + dx, cz + dz);
                    if (occ.TryGetFirstValue(key, out float3 t, out var it))
                        do
                        {
                            float md = (r + t.z) * pack;
                            float ex = t.x - wx, ez = t.y - wz;
                            if (ex * ex + ez * ez < md * md) return true;
                        } while (occ.TryGetNextValue(out t, ref it));
                }
            return false;
        }

        void Register(float wx, float wz, float r)
        {
            occ.Add(new int2((int)math.floor(wx / cell), (int)math.floor(wz / cell)), new float3(wx, wz, r));
        }
    }

    /// <summary>Count-based rock / prop scatter (independent placements, uniform prefab pick).</summary>
    [BurstCompile]
    public struct RockScatterJob : IJob
    {
        public HeightField hf;
        [ReadOnly] public NativeArray<ScatterRuleB> rules;
        [ReadOnly] public NativeArray<float3> villages;
        public NativeList<ScatterInstance> outList;

        public void Execute()
        {
            for (int ri = 0; ri < rules.Length; ri++)
            {
                ScatterRuleB rule = rules[ri];
                if (rule.prefabCount <= 0 || rule.count <= 0) continue;
                var rng = new Random(rule.seed == 0u ? 1u : rule.seed);
                int placed = 0, guard = rule.count * 12;
                while (placed < rule.count && guard-- > 0)
                {
                    float u = rng.NextFloat(), v = rng.NextFloat();
                    if (ScatterMath.InAnyVillage(u, v, hf.sizeX, hf.sizeZ, villages)) continue;
                    float h01 = hf.H01(u, v);
                    if (h01 < ScatterMath.FadeThreshold || h01 < rule.bandLo || h01 > rule.bandHi) continue;
                    float slope = hf.SlopeDeg(u, v);
                    if (ScatterMath.ConditionWeight(rule.where, h01, slope) <= 0.001f) continue;

                    int local = rng.NextInt(rule.prefabCount);
                    int global = rule.prefabStart + local;
                    quaternion rot = rule.alignToNormal != 0 ? ScatterMath.AlignUp(hf.Normal(u, v)) : quaternion.identity;
                    if (rule.randomYRotation != 0) rot = math.mul(rot, quaternion.RotateY(rng.NextFloat() * 2f * math.PI));

                    float3 sc;
                    if (rule.nonUniformScale != 0)
                        sc = new float3(
                            math.lerp(rule.scaleRange.x, rule.scaleRange.y, rng.NextFloat()),
                            math.lerp(rule.scaleRange.x, rule.scaleRange.y, rng.NextFloat()),
                            math.lerp(rule.scaleRange.x, rule.scaleRange.y, rng.NextFloat()));
                    else
                    {
                        float s = math.lerp(rule.scaleRange.x, rule.scaleRange.y, rng.NextFloat());
                        if (s <= 0f) s = 1f;
                        sc = new float3(s);
                    }

                    float3 pos = hf.origin + new float3(u * hf.sizeX, hf.HWorld(u, v) - rule.sink, v * hf.sizeZ);
                    outList.Add(new ScatterInstance { pos = pos, rot = rot, scale = sc, prefab = global });
                    placed++;
                }
            }
        }
    }

    /// <summary>
    /// Flower CLUSTER scatter over ONE chunk's UV window: a jittered density grid of cluster centres, each
    /// gated by band/condition/village, spawns clusterSize flowers within clusterRadius (each re-gated). Runs
    /// per streamed chunk at runtime — Burst removes the per-candidate managed terrain sampling that hitched.
    /// </summary>
    [BurstCompile]
    public struct FlowerScatterJob : IJob
    {
        public HeightField hf;
        [ReadOnly] public NativeArray<ScatterRuleB> rules;
        [ReadOnly] public NativeArray<float> weights;
        [ReadOnly] public NativeArray<float3> villages;
        public float uMin, uMax, vMin, vMax;
        public NativeList<ScatterInstance> outList;

        public void Execute()
        {
            const float jitter = 0.8f;
            for (int ri = 0; ri < rules.Length; ri++)
            {
                ScatterRuleB rule = rules[ri];
                if (rule.prefabCount <= 0) continue;
                float density = rule.density > 0f ? rule.density : 1f;
                float spacing = 10f / math.sqrt(density);
                int gx = math.max(1, (int)math.ceil((uMax - uMin) * hf.sizeX / spacing));
                int gz = math.max(1, (int)math.ceil((vMax - vMin) * hf.sizeZ / spacing));
                var rng = new Random(rule.seed == 0u ? 1u : rule.seed);

                for (int iz = 0; iz < gz; iz++)
                {
                    for (int ix = 0; ix < gx; ix++)
                    {
                        float u = math.saturate(math.lerp(uMin, uMax, (ix + 0.5f + (rng.NextFloat() - 0.5f) * jitter) / gx));
                        float v = math.saturate(math.lerp(vMin, vMax, (iz + 0.5f + (rng.NextFloat() - 0.5f) * jitter) / gz));
                        if (ScatterMath.InAnyVillage(u, v, hf.sizeX, hf.sizeZ, villages)) continue;
                        float h01 = hf.H01(u, v);
                        if (h01 < ScatterMath.FadeThreshold || h01 < rule.bandLo || h01 > rule.bandHi) continue;
                        float slope = hf.SlopeDeg(u, v);
                        if (rng.NextFloat() > ScatterMath.ConditionWeight(rule.where, h01, slope)) continue;

                        int count = rng.NextInt(rule.clusterSize.x, rule.clusterSize.y + 1);
                        float ccx = u * hf.sizeX, ccz = v * hf.sizeZ;
                        for (int i = 0; i < count; i++)
                        {
                            float ang = rng.NextFloat() * math.PI * 2f;
                            float rad = i == 0 ? 0f : rule.clusterRadius * math.sqrt(rng.NextFloat());
                            float wx = ccx + math.cos(ang) * rad;
                            float wz = ccz + math.sin(ang) * rad;
                            float fu = math.saturate(wx / hf.sizeX), fv = math.saturate(wz / hf.sizeZ);
                            float fh01 = hf.H01(fu, fv);
                            if (fh01 < ScatterMath.FadeThreshold || fh01 < rule.bandLo || fh01 > rule.bandHi) continue;
                            if (ScatterMath.InAnyVillage(fu, fv, hf.sizeX, hf.sizeZ, villages)) continue;

                            int local = ScatterMath.PickWeighted(ref rng, weights, rule.weightStart, rule.weightCount, rule.prefabCount);
                            int global = rule.prefabStart + local;
                            float s = math.lerp(rule.scaleRange.x, rule.scaleRange.y, rng.NextFloat());
                            if (s <= 0f) s = 1f;

                            float3 pos = hf.origin + new float3(wx, hf.HWorld(fu, fv) - rule.sink, wz);
                            quaternion rot = rule.alignToNormal != 0 ? ScatterMath.AlignUp(hf.Normal(fu, fv)) : quaternion.identity;
                            if (rule.randomYRotation != 0) rot = math.mul(rot, quaternion.RotateY(rng.NextFloat() * 2f * math.PI));
                            outList.Add(new ScatterInstance { pos = pos, rot = rot, scale = new float3(s), prefab = global });
                        }
                    }
                }
            }
        }
    }
}
