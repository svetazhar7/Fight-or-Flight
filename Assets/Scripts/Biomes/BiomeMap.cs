using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-cell biome classification of the map. The original idea is kept - each
/// point still gets a weighted blend of biomes and a dominant biome - but the
/// climate biomes now come from large <see cref="BiomeRegions"/> chunks while
/// elevation biomes (water, mountains, snow) overlay them by terrain height.
/// </summary>
public class BiomeMap
{
    public struct BiomePoint
    {
        public float height;
        public float temperature;
        public float humidity;
        public Color color;
        public BiomeData dominantBiome;
        public bool isWater;
    }

    private BiomePoint[,] _map;
    private int _resolution;
    private float _blend;
    private BiomeRegions _regions;
    private BiomeData[] _climate;     // active climate biomes (from the regions)
    private BiomeData[] _elevation;   // height-placed biomes
    private BiomeData _oceanBiome;    // the Ocean biome - placed by the waterline, not by percentile
    private float[] _regionWeights;   // scratch buffer reused per point

    public int Resolution => _resolution;
    public BiomeRegions Regions => _regions;
    public BiomeData OceanBiome => _oceanBiome;

    public BiomeMap(int resolution, BiomeData[] biomes, float blend, BiomeRegions regions)
    {
        _resolution = resolution;
        _blend = blend;
        _regions = regions;
        _map = new BiomePoint[resolution, resolution];

        _climate = regions != null ? regions.ActiveBiomes : new BiomeData[0];

        // Elevation biomes classify by terrain-height percentile - EXCEPT Ocean,
        // which is placed wherever terrain sits below the waterline (see SetOcean)
        // so "the Ocean biome" and "where there is water" are one and the same.
        var elev = new List<BiomeData>();
        if (biomes != null)
            foreach (var b in biomes)
            {
                if (b == null || b.placement != BiomePlacement.Elevation) continue;
                if (b.biomeName == "Ocean") { _oceanBiome = b; continue; }
                elev.Add(b);
            }
        _elevation = elev.ToArray();

        _regionWeights = new float[_climate.Length];
    }

    public void SetPoint(int x, int z, float nx, float nz, float height, float temperature, float humidity)
    {
        Color blended = Color.black;
        float total = 0f;
        float maxWeight = -1f;
        BiomeData dominant = null;

        // 1) Elevation biomes by terrain height. The strongest of these also
        //    suppresses the climate region underneath it, so snow stays white on
        //    peaks and water stays blue in basins instead of washing out.
        float elevDominance = 0f;
        for (int j = 0; j < _elevation.Length; j++)
        {
            var b = _elevation[j];
            float w = b.GetWeight(height, temperature, humidity, _blend);
            if (w <= 0f) continue;

            elevDominance = Mathf.Max(elevDominance, Mathf.Clamp01(w));
            blended += b.biomeColor * w;
            total += w;
            if (w > maxWeight) { maxWeight = w; dominant = b; }
        }

        // 2) Climate region biomes, scaled down where an elevation biome dominates.
        if (_regions != null && _climate.Length > 0)
        {
            _regions.GetWeights(nx, nz, _regionWeights);
            float climateScale = 1f - elevDominance;
            for (int i = 0; i < _climate.Length; i++)
            {
                float w = _regionWeights[i] * climateScale * Mathf.Max(0f, _climate[i].strength);
                if (w <= 0f) continue;
                blended += _climate[i].biomeColor * w;
                total += w;
                if (w > maxWeight) { maxWeight = w; dominant = _climate[i]; }
            }
        }

        if (total > 0f)
            blended /= total;
        else
            blended = dominant != null ? dominant.biomeColor : Color.magenta;

        blended.a = 1f;

        _map[x, z] = new BiomePoint
        {
            height = height,
            temperature = temperature,
            humidity = humidity,
            color = blended,
            dominantBiome = dominant
        };
    }

    public void SetWater(int x, int z, bool isWater) => _map[x, z].isWater = isWater;

    /// <summary>
    /// Marks a cell as the Ocean biome (terrain is below the waterline). This is
    /// the single source of truth for "where is the sea": it sets the dominant
    /// biome and colour to Ocean and flags the cell as water, so the water mesh
    /// and the biome map always agree.
    /// </summary>
    public void SetOcean(int x, int z)
    {
        _map[x, z].isWater = true;
        if (_oceanBiome == null) return;
        _map[x, z].dominantBiome = _oceanBiome;
        Color c = _oceanBiome.biomeColor; c.a = 1f;
        _map[x, z].color = c;
    }

    public BiomePoint GetPoint(int x, int z) => _map[x, z];

    public BiomeData GetDominantBiome(int x, int z) => _map[x, z].dominantBiome;

    public Color GetColor(int x, int z) => _map[x, z].color;

    public bool IsWater(int x, int z) => _map[x, z].isWater;
}
