using UnityEngine;

/// <summary>
/// Scatters real prefab GameObjects (trees, rocks, grass) onto a generated
/// Terrain, restricted to the matching biome:
///   Forest    -> trees
///   Mountains -> rocks
///   Plains    -> grass
///
/// We instantiate GameObjects rather than use Unity Terrain TreeInstances / detail
/// layers because those don't render reliably under URP. Placement is fully
/// deterministic from the seed, so every networked peer scatters identically
/// without sending anything over the wire. All instances are parented under a
/// single container child of the terrain, so they're cleared on regeneration.
/// </summary>
public class BiomeScatterer : MonoBehaviour
{
    [Header("Prefabs (assigned by the setup tool)")]
    public GameObject treePrefab;
    public GameObject rockPrefab;
    public GameObject grassPrefab;

    [Header("Trees (Forest)")]
    public string forestBiome = "Forest";
    public int treeCount = 4000;
    public Vector2 treeScale = new Vector2(0.8f, 1.5f);
    [Tooltip("Skip slopes steeper than this (degrees).")]
    public float treeMaxSlope = 32f;

    [Header("Rocks (Mountains)")]
    public string mountainBiome = "Mountains";
    public int rockCount = 1500;
    public Vector2 rockScale = new Vector2(0.6f, 2.4f);
    public float rockMaxSlope = 62f;

    [Header("Grass (Plains)")]
    public string plainsBiome = "Plains";
    public int grassCount = 4000;
    public Vector2 grassScale = new Vector2(0.7f, 1.6f);
    public float grassMaxSlope = 35f;

    [Header("Placement")]
    [Tooltip("How hard to try finding valid spots (attempts = count * this).")]
    [Range(4, 60)] public int attemptsMultiplier = 30;

    [Tooltip("Sink instances slightly into the ground so they don't float.")]
    public float groundOffset = -0.2f;

    private const string ContainerName = "Scatter";

    public void Scatter(WorldData world, Transform worldRoot, int seed)
    {
        if (world == null || world.biomeMap == null || worldRoot == null) return;

        // Fresh container under the world root (destroyed with it on regen).
        Transform existing = worldRoot.Find(ContainerName);
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }
        var container = new GameObject(ContainerName);
        container.transform.SetParent(worldRoot, false);

        System.Random rng = new System.Random(seed * 7919 + 13);

        Place(treePrefab, forestBiome, treeCount, treeScale, treeMaxSlope, world, rng, container.transform);
        Place(rockPrefab, mountainBiome, rockCount, rockScale, rockMaxSlope, world, rng, container.transform);
        Place(grassPrefab, plainsBiome, grassCount, grassScale, grassMaxSlope, world, rng, container.transform);
    }

    private void Place(GameObject prefab, string biomeName, int count, Vector2 scale, float maxSlope,
        WorldData world, System.Random rng, Transform parent)
    {
        if (prefab == null || count <= 0) return;

        BiomeMap map = world.biomeMap;
        int res = map.Resolution;
        Vector3 size = world.worldSize;
        int placed = 0, attempts = 0, maxAttempts = count * Mathf.Max(4, attemptsMultiplier);

        var typeParent = new GameObject(biomeName + "_" + prefab.name);
        typeParent.transform.SetParent(parent, false);

        while (placed < count && attempts < maxAttempts)
        {
            attempts++;
            float u = (float)rng.NextDouble();
            float v = (float)rng.NextDouble();

            int bx = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 0, res - 1);
            int bz = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 0, res - 1);
            if (map.IsWater(bx, bz)) continue;                       // not underwater
            BiomeData b = map.GetDominantBiome(bx, bz);
            if (b == null || b.biomeName != biomeName) continue;     // only the matching biome
            if (world.SampleSteepnessDeg(u, v) > maxSlope) continue; // not on cliffs

            float wx = u * size.x;
            float wz = v * size.z;
            float wy = world.SampleHeight01(u, v) * size.y + groundOffset;

            GameObject go = InstantiatePrefab(prefab, typeParent.transform);
            go.transform.position = new Vector3(wx, wy, wz);
            go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            float s = Mathf.Lerp(scale.x, scale.y, (float)rng.NextDouble());
            go.transform.localScale = Vector3.one * s;
            placed++;
        }
    }

    private static GameObject InstantiatePrefab(GameObject prefab, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
        return Object.Instantiate(prefab, parent);
    }
}
