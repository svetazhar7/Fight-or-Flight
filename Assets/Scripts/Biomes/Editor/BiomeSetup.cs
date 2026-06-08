using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

public static class BiomeSetup
{
    [MenuItem("Tools/Biomes/Create Default Biome Assets")]
    public static void CreateBiomeAssets()
    {
        string folder = "Assets/Biomes";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "Biomes");

        CreateBiome(folder, "Ocean",     new Color(0.05f, 0.15f, 0.55f), 0f,    0.2f,  0f,    0.4f,  0.6f,  1f);
        CreateBiome(folder, "Beach",     new Color(0.93f, 0.87f, 0.55f), 0.18f, 0.28f, 0.4f,  0.8f,  0.2f,  0.6f);
        CreateBiome(folder, "Plains",    new Color(0.55f, 0.80f, 0.35f), 0.25f, 0.55f, 0.3f,  0.7f,  0.3f,  0.7f);
        CreateBiome(folder, "Forest",    new Color(0.13f, 0.45f, 0.13f), 0.28f, 0.60f, 0.2f,  0.6f,  0.55f, 1f);
        CreateBiome(folder, "Desert",    new Color(0.85f, 0.75f, 0.40f), 0.22f, 0.50f, 0.6f,  1f,    0f,    0.3f);
        CreateBiome(folder, "Mountains", new Color(0.55f, 0.55f, 0.55f), 0.55f, 0.80f, 0f,    0.7f,  0f,    0.7f);
        CreateBiome(folder, "Snow",      new Color(0.93f, 0.95f, 1.00f), 0.72f, 1f,    0f,    0.25f, 0f,    1f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[BiomeSetup] Biome assets created in " + folder);
    }

    private static void CreateBiome(string folder, string name,
        Color color,
        float minH, float maxH,
        float minT, float maxT,
        float minM, float maxM)
    {
        string path = $"{folder}/{name}.asset";
        BiomeData existing = AssetDatabase.LoadAssetAtPath<BiomeData>(path);
        if (existing != null) return;

        BiomeData b = ScriptableObject.CreateInstance<BiomeData>();
        b.biomeName = name;
        b.biomeColor = color;
        b.minHeight = minH; b.maxHeight = maxH;
        b.minTemperature = minT; b.maxTemperature = maxT;
        b.minHumidity = minM; b.maxHumidity = maxM;
        AssetDatabase.CreateAsset(b, path);
    }

    [MenuItem("Tools/Biomes/Setup Scene")]
    public static void SetupScene()
    {
        CreateBiomeAssets();

        // BiomeGenerator GameObject
        GameObject gen = new GameObject("BiomeGenerator");
        BiomeGenerator bg = gen.AddComponent<BiomeGenerator>();
        TerrainGenerator tg = gen.AddComponent<TerrainGenerator>();

        // Load biomes
        string[] guids = AssetDatabase.FindAssets("t:BiomeData", new[] { "Assets/Biomes" });
        BiomeData[] biomes = new BiomeData[guids.Length];
        for (int i = 0; i < guids.Length; i++)
            biomes[i] = AssetDatabase.LoadAssetAtPath<BiomeData>(AssetDatabase.GUIDToAssetPath(guids[i]));
        bg.biomes = biomes;

        // Player placeholder
        GameObject player = new GameObject("Player");
        player.transform.position = new Vector3(256f, 50f, 256f);
        player.tag = "Player";

        // BiomeDetector
        GameObject detectorObj = new GameObject("BiomeDetector");
        BiomeDetector detector = detectorObj.AddComponent<BiomeDetector>();
        detector.playerTransform = player.transform;
        detector.biomeGenerator = bg;

        // Canvas + UI
        GameObject canvasObj = new GameObject("BiomeCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject panelObj = new GameObject("BiomePanel");
        panelObj.transform.SetParent(canvasObj.transform, false);
        CanvasGroup cg = panelObj.AddComponent<CanvasGroup>();
        RectTransform panelRT = panelObj.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0f, 1f);
        panelRT.anchorMax = new Vector2(1f, 1f);
        panelRT.pivot = new Vector2(0.5f, 1f);
        panelRT.sizeDelta = new Vector2(0f, 60f);
        panelRT.anchoredPosition = Vector2.zero;

        GameObject textObj = new GameObject("BiomeText");
        textObj.transform.SetParent(panelObj.transform, false);
        Text label = textObj.AddComponent<Text>();
        label.text = "Current Biome: Unknown";
        label.alignment = TextAnchor.MiddleCenter;
        label.fontSize = 24;
        label.color = Color.white;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;

        BiomeUI ui = canvasObj.AddComponent<BiomeUI>();
        ui.biomeDetector = detector;
        ui.biomeLabel = label;
        ui.canvasGroup = cg;

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("[BiomeSetup] Scene setup complete.");
    }
}
