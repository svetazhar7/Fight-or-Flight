using UnityEngine;
using System;

public class BiomeDetector : MonoBehaviour
{
    public Transform playerTransform;
    public BiomeGenerator biomeGenerator;
    public float checkInterval = 0.5f;

    public event Action<BiomeData> OnBiomeChanged;

    private BiomeData _currentBiome;
    private float _timer;

    public BiomeData CurrentBiome => _currentBiome;

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= checkInterval)
        {
            _timer = 0f;
            DetectBiome();
        }
    }

    private void DetectBiome()
    {
        if (playerTransform == null || biomeGenerator == null || biomeGenerator.biomeMap == null)
            return;

        Terrain terrain = biomeGenerator.generatedTerrain;
        if (terrain == null) return;

        TerrainData tData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;
        Vector3 size = tData.size;

        float nx = Mathf.Clamp01((playerTransform.position.x - terrainPos.x) / size.x);
        float nz = Mathf.Clamp01((playerTransform.position.z - terrainPos.z) / size.z);

        int res = biomeGenerator.biomeMap.Resolution;
        int bx = Mathf.Clamp(Mathf.RoundToInt(nx * (res - 1)), 0, res - 1);
        int bz = Mathf.Clamp(Mathf.RoundToInt(nz * (res - 1)), 0, res - 1);

        BiomeData detected = biomeGenerator.biomeMap.GetDominantBiome(bx, bz);
        if (detected != _currentBiome)
        {
            _currentBiome = detected;
            OnBiomeChanged?.Invoke(_currentBiome);
        }
    }
}
