using UnityEngine;

[CreateAssetMenu(fileName = "BiomeData", menuName = "Biomes/Biome Data")]
public class BiomeData : ScriptableObject
{
    [Header("Identity")]
    public string biomeName = "Unknown";
    public Color biomeColor = Color.white;

    [Header("Thresholds")]
    [Range(0f, 1f)] public float minHeight = 0f;
    [Range(0f, 1f)] public float maxHeight = 1f;
    [Range(0f, 1f)] public float minTemperature = 0f;
    [Range(0f, 1f)] public float maxTemperature = 1f;
    [Range(0f, 1f)] public float minHumidity = 0f;
    [Range(0f, 1f)] public float maxHumidity = 1f;

    [Header("Influence")]
    [Tooltip("Relative weight multiplier so some biomes can dominate ties.")]
    public float strength = 1f;

    // blend = soft falloff band (in 0..1 units) extending beyond each range edge.
    // Larger blend -> biomes bleed further into one another -> smoother transitions.
    public float GetWeight(float height, float temperature, float humidity, float blend)
    {
        float hw = GetRangeWeight(height, minHeight, maxHeight, blend);
        float tw = GetRangeWeight(temperature, minTemperature, maxTemperature, blend);
        float mw = GetRangeWeight(humidity, minHumidity, maxHumidity, blend);
        return hw * tw * mw * Mathf.Max(0f, strength);
    }

    // Plateau of full weight inside [min,max], with a smooth shoulder of width
    // 'blend' on each side. Beyond min-blend / max+blend the weight is zero.
    private float GetRangeWeight(float value, float min, float max, float blend)
    {
        if (value >= min && value <= max) return 1f;
        if (blend <= 0f) return 0f;

        if (value < min)
            return Mathf.SmoothStep(0f, 1f, 1f - (min - value) / blend);
        return Mathf.SmoothStep(0f, 1f, 1f - (value - max) / blend);
    }
}
