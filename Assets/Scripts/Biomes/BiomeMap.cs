using UnityEngine;

public class BiomeMap
{
    public struct BiomePoint
    {
        public float height;
        public float temperature;
        public float humidity;
        public Color color;
        public BiomeData dominantBiome;
    }

    private BiomePoint[,] _map;
    private int _resolution;
    private BiomeData[] _biomes;
    private float _blend;

    public int Resolution => _resolution;

    public BiomeMap(int resolution, BiomeData[] biomes, float blend)
    {
        _resolution = resolution;
        _biomes = biomes;
        _blend = blend;
        _map = new BiomePoint[resolution, resolution];
    }

    public void SetPoint(int x, int z, float height, float temperature, float humidity)
    {
        var point = new BiomePoint
        {
            height = height,
            temperature = temperature,
            humidity = humidity
        };

        float totalWeight = 0f;
        Color blendedColor = Color.black;
        float maxWeight = -1f;
        BiomeData dominant = _biomes.Length > 0 ? _biomes[0] : null;

        foreach (var biome in _biomes)
        {
            float w = biome.GetWeight(height, temperature, humidity, _blend);
            totalWeight += w;
            blendedColor += biome.biomeColor * w;
            if (w > maxWeight)
            {
                maxWeight = w;
                dominant = biome;
            }
        }

        if (totalWeight > 0f)
            blendedColor /= totalWeight;
        else
            blendedColor = _biomes.Length > 0 ? _biomes[0].biomeColor : Color.magenta;

        blendedColor.a = 1f;
        point.color = blendedColor;
        point.dominantBiome = dominant;
        _map[x, z] = point;
    }

    public BiomePoint GetPoint(int x, int z) => _map[x, z];

    public BiomeData GetDominantBiome(int x, int z) => _map[x, z].dominantBiome;

    public Color GetColor(int x, int z) => _map[x, z].color;
}
