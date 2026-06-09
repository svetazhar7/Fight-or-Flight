using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Partitions the map into a few large, organic biome regions (Minecraft-style
/// big chunks). A limited subset of climate biomes is chosen from the available
/// list and spread across Voronoi cells. The lookup coordinates are domain-warped
/// with low-frequency noise so the borders meander naturally instead of forming
/// straight Voronoi edges, and the two nearest cells are blended for soft seams.
///
/// The original idea is preserved: which biome a cell gets is still decided by
/// climate (temperature + humidity sampled at the cell's centre), so deserts land
/// in hot/dry areas and forests in humid ones - only now each climate "vote" wins
/// a whole region rather than a single pixel, giving big contiguous biomes.
/// </summary>
public class BiomeRegions
{
    private readonly BiomeData[] _activeBiomes;   // chosen climate biomes (the only ones used this map)
    private readonly Vector2[] _seeds;            // Voronoi cell centres in 0..1 space
    private readonly int[] _seedBiome;            // index into _activeBiomes per seed
    private readonly float _warpAmount;
    private readonly Vector2 _warpOffset;
    private readonly float _borderBlend;

    public BiomeData[] ActiveBiomes => _activeBiomes;
    public int Count => _activeBiomes.Length;

    public BiomeRegions(
        IList<BiomeData> climateBiomes,
        int biomesPerMap,
        int cellsPerAxis,
        float warpAmount,
        float borderBlend,
        System.Random rng,
        System.Func<float, float, float> sampleTemperature,
        System.Func<float, float, float> sampleHumidity)
    {
        _warpAmount = Mathf.Max(0f, warpAmount);
        _borderBlend = Mathf.Max(0.001f, borderBlend);
        _warpOffset = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);

        // 1) Choose the limited subset of climate biomes used across the whole map.
        _activeBiomes = ChooseSubset(climateBiomes, biomesPerMap, rng);

        // 2) Scatter Voronoi seeds on a jittered grid for evenly spread, big cells.
        int n = Mathf.Max(2, cellsPerAxis);
        _seeds = new Vector2[n * n];
        _seedBiome = new int[n * n];
        float cell = 1f / n;
        for (int gz = 0; gz < n; gz++)
        {
            for (int gx = 0; gx < n; gx++)
            {
                int i = gz * n + gx;
                float jx = (float)rng.NextDouble();
                float jz = (float)rng.NextDouble();
                _seeds[i] = new Vector2((gx + jx) * cell, (gz + jz) * cell);
            }
        }

        // 3) Assign each seed the best-matching climate biome at its location.
        AssignSeedBiomes(rng, sampleTemperature, sampleHumidity);
    }

    private static BiomeData[] ChooseSubset(IList<BiomeData> source, int count, System.Random rng)
    {
        var pool = new List<BiomeData>();
        if (source != null)
            foreach (var b in source)
                if (b != null) pool.Add(b);

        if (pool.Count == 0) return new BiomeData[0];

        count = Mathf.Clamp(count, 1, pool.Count);

        // Fisher-Yates partial shuffle for a seeded, repeatable random subset.
        for (int i = 0; i < count; i++)
        {
            int j = i + rng.Next(pool.Count - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var chosen = new BiomeData[count];
        for (int i = 0; i < count; i++) chosen[i] = pool[i];
        return chosen;
    }

    private void AssignSeedBiomes(
        System.Random rng,
        System.Func<float, float, float> sampleTemperature,
        System.Func<float, float, float> sampleHumidity)
    {
        if (_activeBiomes.Length == 0) return;

        var used = new bool[_activeBiomes.Length];
        for (int s = 0; s < _seeds.Length; s++)
        {
            float t = sampleTemperature != null ? sampleTemperature(_seeds[s].x, _seeds[s].y) : 0.5f;
            float h = sampleHumidity != null ? sampleHumidity(_seeds[s].x, _seeds[s].y) : 0.5f;

            int best = 0;
            float bestW = -1f;
            for (int b = 0; b < _activeBiomes.Length; b++)
            {
                // Climate-only weight: ignore height so the region choice is driven
                // by temperature/humidity, exactly like the original classifier.
                var bio = _activeBiomes[b];
                float tw = RangeWeight(t, bio.minTemperature, bio.maxTemperature);
                float mw = RangeWeight(h, bio.minHumidity, bio.maxHumidity);
                float w = tw * mw * Mathf.Max(0.0001f, bio.strength);
                if (w > bestW) { bestW = w; best = b; }
            }
            _seedBiome[s] = best;
            used[best] = true;
        }

        // Coverage pass: make sure every chosen biome appears at least once by
        // reassigning random seeds to any biome that got left out.
        for (int b = 0; b < _activeBiomes.Length; b++)
        {
            if (used[b]) continue;
            int s = rng.Next(_seeds.Length);
            _seedBiome[s] = b;
        }
    }

    // Soft membership (1 inside the range, falling off over a fixed band) used only
    // for assigning seeds; the live blend uses the Voronoi distance instead.
    private static float RangeWeight(float value, float min, float max)
    {
        const float band = 0.15f;
        if (value >= min && value <= max) return 1f;
        if (value < min) return Mathf.Clamp01(1f - (min - value) / band);
        return Mathf.Clamp01(1f - (value - max) / band);
    }

    /// <summary>
    /// Fills <paramref name="weights"/> (length == Count) with the blend weights of
    /// each active climate biome at the given normalized coordinate. Inside a region
    /// the nearest biome gets ~1; near a border it blends with the closest neighbour
    /// of a different biome.
    /// </summary>
    public void GetWeights(float nx, float nz, float[] weights)
    {
        for (int i = 0; i < weights.Length; i++) weights[i] = 0f;
        if (_seeds.Length == 0 || _activeBiomes.Length == 0) return;

        // Domain warp so the cell boundaries wander instead of being straight.
        float wx = nx, wz = nz;
        if (_warpAmount > 0f)
        {
            float warpFreq = 3.5f;
            wx += (Mathf.PerlinNoise(nx * warpFreq + _warpOffset.x, nz * warpFreq + _warpOffset.y) - 0.5f) * _warpAmount;
            wz += (Mathf.PerlinNoise(nx * warpFreq + _warpOffset.y, nz * warpFreq + _warpOffset.x) - 0.5f) * _warpAmount;
        }

        // Nearest seed, and nearest seed belonging to a different biome.
        int nearest = 0;
        float dNear = float.MaxValue;
        for (int s = 0; s < _seeds.Length; s++)
        {
            float dx = _seeds[s].x - wx;
            float dz = _seeds[s].y - wz;
            float d = dx * dx + dz * dz;
            if (d < dNear) { dNear = d; nearest = s; }
        }

        int biomeA = _seedBiome[nearest];
        float dOther = float.MaxValue;
        int biomeB = -1;
        for (int s = 0; s < _seeds.Length; s++)
        {
            if (_seedBiome[s] == biomeA) continue;
            float dx = _seeds[s].x - wx;
            float dz = _seeds[s].y - wz;
            float d = dx * dx + dz * dz;
            if (d < dOther) { dOther = d; biomeB = _seedBiome[s]; }
        }

        if (biomeB < 0)
        {
            // Only one biome present anywhere.
            weights[biomeA] = 1f;
            return;
        }

        // Blend by the gap between the two nearest different-biome distances.
        float da = Mathf.Sqrt(dNear);
        float db = Mathf.Sqrt(dOther);
        float t = Mathf.Clamp01((db - da) / _borderBlend); // 0 at border -> 1 deep inside A
        float wA = 0.5f + 0.5f * t;
        weights[biomeA] += wA;
        weights[biomeB] += 1f - wA;
    }
}
