#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace IslandSystem.EditorTools
{
    /// <summary>
    /// Custom inspector for <see cref="BiomeAssetManifest"/> that adds a "Scan Biome Folder" button.
    /// Scanning looks next to the manifest for <c>Textures/</c> and <c>Props/</c>, turns each texture into
    /// a reusable <see cref="TerrainLayer"/> (cached under <c>Layers/</c>), wires up every prefab in
    /// <c>Props/</c>, and authors a sensible default set of texturing rules if none exist yet.
    /// </summary>
    [CustomEditor(typeof(BiomeAssetManifest))]
    public class BiomeFolderScannerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manifest = (BiomeAssetManifest)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Drop ground/cliff textures into 'Textures/' and prop prefabs into 'Props/' next to this " +
                "asset, then press the button. TerrainLayers are (re)built from the textures and default " +
                "texturing rules are created if you have none.",
                MessageType.Info);

            if (GUILayout.Button("Scan Biome Folder", GUILayout.Height(30)))
                ScanBiomeFolder(manifest);
        }

        static void ScanBiomeFolder(BiomeAssetManifest manifest)
        {
            string manifestPath = AssetDatabase.GetAssetPath(manifest);
            string biomeDir = Path.GetDirectoryName(manifestPath).Replace("\\", "/");
            string texDir = biomeDir + "/Textures";
            string propDir = biomeDir + "/Props";
            string layerDir = biomeDir + "/Layers";

            // ---- Terrain layers from textures ----
            var layers = new List<TerrainLayer>();
            if (AssetDatabase.IsValidFolder(texDir))
            {
                if (!AssetDatabase.IsValidFolder(layerDir))
                    AssetDatabase.CreateFolder(biomeDir, "Layers");

                var texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { texDir });
                foreach (var guid in texGuids.OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
                {
                    string texPath = AssetDatabase.GUIDToAssetPath(guid);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    if (tex == null) continue;

                    string layerName = Path.GetFileNameWithoutExtension(texPath) + ".terrainlayer";
                    string layerPath = layerDir + "/" + layerName;

                    var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                    if (layer == null)
                    {
                        layer = new TerrainLayer();
                        AssetDatabase.CreateAsset(layer, layerPath);
                    }
                    layer.diffuseTexture = tex;
                    if (layer.tileSize == Vector2.zero) layer.tileSize = new Vector2(15f, 15f);
                    EditorUtility.SetDirty(layer);
                    layers.Add(layer);
                }
            }
            else
            {
                Debug.LogWarning($"[BiomeScanner] No 'Textures' folder at {texDir}.");
            }
            manifest.terrainLayers = layers.ToArray();

            // ---- Props from prefabs ----
            var props = new List<GameObject>();
            if (AssetDatabase.IsValidFolder(propDir))
            {
                var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { propDir });
                foreach (var guid in prefabGuids.OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (go != null) props.Add(go);
                }
            }
            manifest.props = props.ToArray();

            // ---- Default texturing rules (only if none authored yet) ----
            if ((manifest.texturingRules == null || manifest.texturingRules.Length == 0) && layers.Count > 0)
                manifest.texturingRules = BuildDefaultRules(layers.Count);

            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[BiomeScanner] '{manifest.biomeName}': {layers.Count} terrain layer(s), {props.Count} prop(s).", manifest);
        }

        /// <summary>
        /// Stacks layers from low/flat (layer 0) to high, with the LAST layer reserved for cliffs
        /// (steep slope, any height) — matches the sand -> rock -> cliff intent for Grand Canyon biomes.
        /// </summary>
        static TexturingRule[] BuildDefaultRules(int count)
        {
            var rules = new TexturingRule[count];
            if (count == 1)
            {
                rules[0] = TexturingRule.Default(0, 0f, 1f, 0f, 90f);
                return rules;
            }

            int bandCount = count - 1;               // last layer is the cliff
            float step = 1f / bandCount;
            for (int i = 0; i < bandCount; i++)
            {
                float minH = Mathf.Clamp01(i * step - 0.1f);
                float maxH = Mathf.Clamp01((i + 1) * step + 0.1f);
                rules[i] = TexturingRule.Default(i, minH, maxH, 0f, 40f);
                rules[i].label = $"band {i}";
            }
            // Cliff layer: steep faces at any height.
            rules[count - 1] = TexturingRule.Default(count - 1, 0f, 1f, 38f, 90f);
            rules[count - 1].label = "cliff";
            rules[count - 1].weight = 1.5f;
            return rules;
        }
    }
}
#endif
