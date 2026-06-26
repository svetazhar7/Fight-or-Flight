#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Pinwheel.Poseidon
{
    public static class WaterUtilities
    {
        public struct RippleParams
        {
            public Texture2D noiseTex;
            public float time;
            public float rippleHeight;
            public float rippleSpeed;
            public float rippleScale;
        }

        public static RippleParams ExtractRippleParams(Material mat)
        {
            RippleParams ripple = new RippleParams();
            if (mat.HasProperty(PMat.NOISE_TEX))
                ripple.noiseTex = mat.GetTexture(PMat.NOISE_TEX) as Texture2D;
            if (mat.HasProperty(PMat.TIME))
                ripple.time = mat.GetFloat(PMat.TIME);
            if (mat.HasProperty(PMat.RIPPLE_HEIGHT))
                ripple.rippleHeight = mat.GetFloat(PMat.RIPPLE_HEIGHT);
            if (mat.HasProperty(PMat.RIPPLE_SPEED))
                ripple.rippleSpeed = mat.GetFloat(PMat.RIPPLE_SPEED);
            if (mat.HasProperty(PMat.RIPPLE_NOISE_SCALE))
                ripple.rippleScale = mat.GetFloat(PMat.RIPPLE_NOISE_SCALE);

            return ripple;
        }

        public static Vector3 CalculateRippleOffsetWS(Vector3 positionWS, RippleParams rippleParams)
        {
            Vector2 noisePos = new Vector2(positionWS.x, positionWS.z);

            rippleParams.rippleScale *= 0.01f;
            rippleParams.rippleSpeed *= 0.1f;

            float time = rippleParams.time;
            float rippleSpeed = rippleParams.rippleSpeed;
            float rippleScale = rippleParams.rippleScale;

            Vector2 baseNoiseUV = (noisePos - Vector2.one * time * rippleSpeed) * rippleScale;
            Vector2 fadeNoiseUV = (noisePos + Vector2.one * time * rippleSpeed * 3) * rippleScale;

            Texture2D noiseTex = rippleParams.noiseTex;
            float noiseBase = noiseTex.GetPixelBilinear(baseNoiseUV.x, baseNoiseUV.y).r;
            float noiseFade = noiseTex.GetPixelBilinear(fadeNoiseUV.x, fadeNoiseUV.y).r;
            float noiseCombined = Mathf.Abs(noiseBase - noiseFade);
            noiseCombined = Mathf.Lerp(-1, 1, noiseCombined);

            Vector3 offsetWS = new Vector3(0, noiseCombined * rippleParams.rippleHeight, 0);
            return offsetWS;
        }

        public struct WaveParams
        {
            public Texture2D noiseTex;
            public float time;
            public Vector2 waveDirection;
            public float waveSpeed;
            public float waveDeform;
            public float waveLength;
            public float waveSteepness;
            public float waveHeight;
        }

        public static WaveParams ExtractWaveParams(Material mat)
        {
            WaveParams wave = new WaveParams();
            if (mat.HasProperty(PMat.NOISE_TEX))
                wave.noiseTex = mat.GetTexture(PMat.NOISE_TEX) as Texture2D;
            if (mat.HasProperty(PMat.TIME))
                wave.time = mat.GetFloat(PMat.TIME);
            if (mat.HasProperty(PMat.WAVE_DIRECTION))
                wave.waveDirection = mat.GetVector(PMat.WAVE_DIRECTION);
            if (mat.HasProperty(PMat.WAVE_SPEED))
                wave.waveSpeed = mat.GetFloat(PMat.WAVE_SPEED);
            if (mat.HasProperty(PMat.WAVE_DEFORM))
                wave.waveDeform = mat.GetFloat(PMat.WAVE_DEFORM);
            if (mat.HasProperty(PMat.WAVE_LENGTH))
                wave.waveLength = mat.GetFloat(PMat.WAVE_LENGTH);
            if (mat.HasProperty(PMat.WAVE_STEEPNESS))
                wave.waveSteepness = mat.GetFloat(PMat.WAVE_STEEPNESS);
            if (mat.HasProperty(PMat.WAVE_HEIGHT))
                wave.waveHeight = mat.GetFloat(PMat.WAVE_HEIGHT);

            return wave;
        }

        public static Vector3 CalculateWaveOffsetWS(Vector3 positionWS, WaveParams waveParams)
        {
            float time = waveParams.time;
            float waveSpeed = waveParams.waveSpeed;
            float waveLength = waveParams.waveLength;
            float waveHeight = waveParams.waveHeight;
            float waveDeform = waveParams.waveDeform;
            float waveSteepness = waveParams.waveSteepness;
            Vector2 waveDir = waveParams.waveDirection;
            Texture2D noiseTex = waveParams.noiseTex;

            Vector2 worldXZ = new Vector2(positionWS.x, positionWS.z);
            Vector2 noisePos = (worldXZ - 2 * waveSpeed * waveDir * time) * 0.001f;
            float noise = noiseTex.GetPixelBilinear(noisePos.x, noisePos.y).r * waveDeform;

            float dotValue = Vector2.Dot(worldXZ, waveDir);
            float t = Frac((dotValue - waveSpeed * time) / waveLength - noise * 0.25f);

            float waveCurve = SampleWaveCurve(t, waveSteepness, noise);

            Vector3 offset = new Vector3(0, waveHeight * waveCurve, 0);
            return offset;
        }

        private static float Frac(float v)
        {
            return v - Mathf.Floor(v);
        }

        private static float SampleWaveCurve(float t, float waveSteepness, float noise)
        {
            Vector2 left = new Vector2(0, 0);
            Vector2 right = new Vector2(1, 0);
            Vector2 middle = new Vector2(Mathf.Lerp(0.5f, 0.95f, waveSteepness), 1);

            Vector2 anchor0 = left * ((t < middle.x) ? 1 : 0) + middle * ((t >= middle.x) ? 1 : 0);
            Vector2 anchor1 = middle * ((t < middle.x) ? 1 : 0) + right * ((t >= middle.x) ? 1 : 0);
            float tangentLength = 0.5f;
            Vector2 tangent0 = new Vector2(tangentLength, 0) * ((t < middle.x) ? 1 : 0) + new Vector2(middle.x + tangentLength, middle.y) * ((t >= middle.x) ? 1 : 0);
            Vector2 tangent1 = new Vector2(middle.x - tangentLength, middle.y) * ((t < middle.x) ? 1 : 0) + new Vector2(1 - tangentLength, 0) * ((t >= middle.x) ? 1 : 0);
            float splineT = (t - anchor0.x) / (anchor1.x - anchor0.x);

            Vector2 p = SampleSpline(splineT, anchor0, anchor1, tangent0, tangent1);
            p.y -= Mathf.Lerp(0.5f, 1, noise * 0.5f + 0.5f);

            return p.y;
        }

        private static Vector2 SampleSpline(float t, Vector2 anchor0, Vector2 anchor1, Vector2 tangent0, Vector2 tangent1)
        {
            float oneMinusT = 1 - t;
            Vector2 p =
                oneMinusT * oneMinusT * oneMinusT * anchor0 +
                3 * oneMinusT * oneMinusT * t * tangent0 +
                3 * oneMinusT * t * t * tangent1 +
                t * t * t * anchor1;
            return p;
        }

        public struct WaveMaskParams
        {
            public Texture2D waveMaskTex;
            public Vector4 waveMaskBounds;
        }

        public static WaveMaskParams ExtractWaveMaskParams(Material mat)
        {
            WaveMaskParams waveMaskParams = new WaveMaskParams();
            if (mat.HasProperty(PMat.WAVE_MASK))
                waveMaskParams.waveMaskTex = mat.GetTexture(PMat.WAVE_MASK) as Texture2D;
            if (mat.HasProperty(PMat.WAVE_MASK_BOUNDS))
                waveMaskParams.waveMaskBounds = mat.GetVector(PMat.WAVE_MASK_BOUNDS);

            return waveMaskParams;
        }

        public static Vector3 ApplyWaveMask(Vector3 positionWS, Vector3 waveOffsetWS, WaveMaskParams waveMaskParams)
        {
            Texture2D waveMask = waveMaskParams.waveMaskTex;
            Vector4 waveMaskBounds = waveMaskParams.waveMaskBounds;

            Vector2 maskUV = new Vector2
            (
                (positionWS.x - waveMaskBounds.x) / (waveMaskBounds.z - waveMaskBounds.x),
                (positionWS.z - waveMaskBounds.y) / (waveMaskBounds.w - waveMaskBounds.y)
            );

            bool isInBounds = (maskUV.x >= 0) && (maskUV.x <= 1) && (maskUV.y >= 0) && (maskUV.y <= 1);
            float inBoundsF = isInBounds ? 1f : 0f;

            Vector4 mask = waveMask.GetPixelBilinear(maskUV.x, maskUV.y);
            float heightMask = Mathf.Lerp(1, mask.w, inBoundsF);
            Vector3 newOffset = waveOffsetWS;
            newOffset.y *= heightMask;

            return newOffset;
        }

        private static List<MeshRenderer> tempRendererContainer = new List<MeshRenderer>();

        /// <summary>
        /// Check if any renderer of a water body contains a point in world space, ignoring Y-coordinate.
        /// </summary>
        /// <param name="water"></param>
        /// <param name="pointWS"></param>
        /// <returns></returns>
        public static bool RenderersContainPointXZ(this PoseidonWaterBody water, Vector3 pointWS)
        {
            water.GetRenderers(tempRendererContainer);
            foreach (MeshRenderer mr in tempRendererContainer)
            {
                Bounds boundsWS = mr.bounds;
                if (pointWS.x >= boundsWS.min.x && pointWS.x <= boundsWS.max.x &&
                    pointWS.z >= boundsWS.min.z && pointWS.z <= boundsWS.max.z)
                {
                    return true;
                }
            }
            return false;
        }

        public static Bounds GetSuperBoundsWS(this PoseidonWaterBody water)
        {
            water.GetRenderers(tempRendererContainer);
            Bounds b = new Bounds();
            if (tempRendererContainer.Count > 0)
            {
                b = tempRendererContainer[0].bounds;
                foreach (MeshRenderer mr in tempRendererContainer)
                {
                    b.Encapsulate(mr.bounds);
                }
            }

            return b;
        }

        public static void GenerateCompositeBoundingBoxes(this PoseidonWaterBody water, List<Bounds> boundsWSContainer)
        {
            boundsWSContainer.Clear();
            if (water is TileableWater tileableWater)
            {
                CompositeBBGenerator.GenerateBoundsWS(tileableWater, boundsWSContainer);
            }
            else if (water is AreaWater areaWater)
            {
                boundsWSContainer.Add(areaWater.GetSuperBoundsWS());
            }
            else
            {
                throw new System.NotImplementedException($"Composite Bounds for {water.GetType().Name} is not implemeted yet.");
            }
        }
    }

    internal static class CompositeBBGenerator
    {
        internal static void GenerateBoundsWS(TileableWater water, List<Bounds> container)
        {
            int[,] grid = null;
            int indexOffsetX, indexOffsetY;
            ExtractGrid(water, out grid, out indexOffsetX, out indexOffsetY);
            MarkNonEmptyCells(water, grid, indexOffsetX, indexOffsetY);
            Partitioning(grid);
            ComputeBounds(water, grid, indexOffsetX, indexOffsetY, container);
        }

        private static void ExtractGrid(TileableWater water, out int[,] grid, out int indexOffsetX, out int indexOffsetY)
        {
            int minIndexX = water.tiles.Min(t => t.index.x);
            int minIndexY = water.tiles.Min(t => t.index.y);
            int maxIndexX = water.tiles.Max(t => t.index.x);
            int maxIndexY = water.tiles.Max(t => t.index.y);

            int dim0 = maxIndexY - minIndexY + 1;
            int dim1 = maxIndexX - minIndexX + 1;

            grid = new int[dim0, dim1];

            for (int y = dim0 - 1; y >= 0; --y)
            {
                for (int x = 0; x < dim1; ++x)
                {
                    grid[y, x] = -1;
                }
            }

            indexOffsetX = -minIndexX;
            indexOffsetY = -minIndexY;
        }

        private static void MarkNonEmptyCells(TileableWater water, int[,] grid, int indexOffsetX, int indexOffsetY)
        {
            foreach (var t in water.tiles)
            {
                grid[t.index.y + indexOffsetY, t.index.x + indexOffsetX] = 0;
            }
        }

        private static void Partitioning(int[,] grid)
        {
            int groupId = 0;
            for (int y = 0; y < grid.GetLength(0); ++y)
            {
                for (int x = 0; x < grid.GetLength(1); ++x)
                {
                    if (grid[y, x] != 0)
                        continue;
                    groupId += 1;
                    int limitX;
                    FloodHorizontal(grid, x, y, groupId, out limitX);
                    FloodVertical(grid, x, y, groupId, limitX);
                }
            }
        }

        private static void FloodHorizontal(int[,] grid, int indexX, int indexY, int groupId, out int limitX)
        {
            int x;
            for (x = indexX; x < grid.GetLength(1); ++x)
            {
                if (grid[indexY, x] == 0)
                {
                    grid[indexY, x] = groupId;
                }
                else
                {
                    break;
                }
            }
            limitX = x;
        }

        private static void FloodVertical(int[,] grid, int indexX, int indexY, int groupId, int limitX)
        {
            for (int y = indexY; y < grid.GetLength(0) - 1; ++y)
            {
                for (int x = indexX; x < limitX; ++x)
                {
                    if (grid[y + 1, x] != 0)
                    {
                        return;
                    }
                }

                for (int x = indexX; x < limitX; ++x)
                {
                    grid[y + 1, x] = groupId;
                }
            }
        }

        private static void ComputeBounds(TileableWater water, int[,] grid, int indexOffsetX, int indexOffsetY, List<Bounds> container)
        {
            Bounds superBounds = water.GetSuperBoundsWS();
            int dimY = grid.GetLength(0);
            float fDimY = (float)dimY;
            int dimX = grid.GetLength(1);
            float fDimX = (float)dimX;

            Dictionary<int, Bounds> boundsByGroupId = new Dictionary<int, Bounds>();
            for (int y = 0; y < dimY; ++y)
            {
                for (int x = 0; x < dimX; ++x)
                {
                    int groupId = grid[y, x];
                    if (groupId <= 0) continue;

                    Bounds tileBounds = new Bounds();
                    Vector3 min = new Vector3(
                        Mathf.Lerp(superBounds.min.x, superBounds.max.x, x * 1.0f / dimX),
                        superBounds.min.y,
                        Mathf.Lerp(superBounds.min.z, superBounds.max.z, y * 1.0f / dimY));

                    Vector3 max = new Vector3(
                        Mathf.Lerp(superBounds.min.x, superBounds.max.x, (x + 1) * 1.0f / dimX),
                        superBounds.max.y,
                        Mathf.Lerp(superBounds.min.z, superBounds.max.z, (y + 1) * 1.0f / dimY));

                    tileBounds.SetMinMax(min, max);

                    Bounds groupBounds;
                    if (boundsByGroupId.ContainsKey(groupId))
                    {
                        groupBounds = boundsByGroupId[groupId];
                    }
                    else
                    {
                        groupBounds = tileBounds;
                    }
                    groupBounds.Encapsulate(tileBounds);

                    boundsByGroupId[groupId] = groupBounds;
                }
            }

            container.Clear();
            container.AddRange(boundsByGroupId.Values);
        }
    }
}

#endif