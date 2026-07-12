using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Burst per-instance visibility cull for one <see cref="IslandTreeRenderer"/> batch, replacing the old
    /// single-threaded main-thread loop. For every instance it runs the SAME three tests the managed code did —
    /// sphere-vs-frustum, distance density LOD (keep-fraction vs a per-instance hash), and terrain occlusion
    /// (sight-line vs the island height, sampled from a native <see cref="HeightField"/> instead of the managed
    /// <c>Terrain.SampleHeight</c>) — and appends the surviving world matrices to <see cref="outList"/> (a
    /// pre-sized NativeList via its parallel writer). The renderer then hands that list straight to
    /// <c>Graphics.RenderMeshInstanced</c>. Running this across worker threads gets the ~34k-instance cull off the
    /// main thread, which was the frame's CPU bottleneck when a whole island sits in the frustum.
    /// </summary>
    [BurstCompile]
    public struct TreeCullJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Matrix4x4> matrices;
        [ReadOnly] public NativeArray<float3> centers;
        [ReadOnly] public NativeArray<float> rand;
        [ReadOnly] public NativeArray<float4> planes;   // 6 frustum planes: xyz = inward normal, w = plane distance

        public float3 camPos;
        public float radius, topOffset;                 // per-batch: cull sphere radius, canopy height for the sight-line
        public float lodNear, span, lodMinKeep, near2;  // distance density LOD
        public float minDist2, maxDist2;                // distance window: mesh = [0..impostor], impostor = [impostor..∞]
        public int useFrustum;                          // 1 = camera view (frustum + LOD + occlusion); 0 = SHADOW casters
        public int doOcclusion;
        public float occlMin2, occlusionBias;
        public HeightField hf;                          // island height for occlusion (inner array is [ReadOnly])

        public NativeList<Matrix4x4>.ParallelWriter outList;

        public void Execute(int i)
        {
            float3 c = centers[i];

            // per-instance frustum (sphere) cull — SKIPPED for shadow casters. Shadows must come from geometry that
            // is off-screen / behind the camera too; culling casters to the camera frustum is what makes tree
            // shadows pop in and out as the player turns. Shadow casters use a pure yaw-independent distance cull.
            if (useFrustum != 0)
            {
                for (int p = 0; p < 6; p++)
                {
                    float4 pl = planes[p];
                    if (math.dot(pl.xyz, c) + pl.w < -radius) return;
                }
            }

            float dx = c.x - camPos.x, dz = c.z - camPos.z;
            float d2 = dx * dx + dz * dz;
            if (d2 < minDist2 || d2 > maxDist2) return;  // distance window (mesh near / impostor far / shadow radius)
            if (d2 > near2)                              // distance density LOD (near2 = MAX for shadows = never thin out)
            {
                float t = math.saturate((math.sqrt(d2) - lodNear) / span);
                float keep = math.lerp(1f, lodMinKeep, t);
                if (rand[i] > keep) return;
            }

            if (doOcclusion != 0 && d2 > occlMin2 && Occluded(camPos, c, topOffset)) return;

            outList.AddNoResize(matrices[i]);
        }

        /// <summary>Sight-line vs island height, matching the managed <c>Occluded</c> (world XZ → UV → HeightField).</summary>
        bool Occluded(float3 cam, float3 tree, float canopy)
        {
            float targetY = tree.y + canopy;
            for (float t = 0.75f; t > 0.25f; t -= 0.25f)
            {
                float sx = math.lerp(cam.x, tree.x, t);
                float sz = math.lerp(cam.z, tree.z, t);
                float u = (sx - hf.origin.x) / hf.sizeX;
                float v = (sz - hf.origin.z) / hf.sizeZ;
                float ground = hf.H01(u, v) * hf.sizeY + hf.origin.y;
                float sight = math.lerp(cam.y, targetY, t);
                if (ground > sight + occlusionBias) return true;
            }
            return false;
        }
    }
}
