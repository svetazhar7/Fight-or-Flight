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
    /// a reusable <see cref="TerrainLayer"/> (cached under <c>Layers/</c>) and wires every prefab in
    /// <c>Props/</c> into a spawn rule. It MERGES: existing texture-layer conditions and spawn-rule tuning
    /// are preserved; only newly-found assets are added.
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
                "Drop assets next to this manifest: textures → 'Textures/', props → 'Props/', tree prefabs → " +
                "'Trees/' (Unity Terrain trees), rock prefabs → 'Rocks/' (GameObjects). Press the button: new " +
                "assets become condition-driven rules; anything already tuned here is kept. After scanning, " +
                "press Generate on the ArchipelagoGenerator to rebake.",
                MessageType.Info);

            if (GUILayout.Button("Scan Biome Folder", GUILayout.Height(30)))
                ScanBiomeFolder(manifest);
        }

        static void ScanBiomeFolder(BiomeAssetManifest manifest)
        {
            string manifestPath = AssetDatabase.GetAssetPath(manifest);
            string biomeDir = Path.GetDirectoryName(manifestPath).Replace("\\", "/");
            string layerDir = biomeDir + "/Layers";

            EnsureFolder(biomeDir, "Trees");
            EnsureFolder(biomeDir, "Rocks");

            Undo.RecordObject(manifest, "Scan Biome Folder");

            manifest.spawnRules ??= new List<ObjectSpawnRule>();
            manifest.treeRules ??= new List<ObjectSpawnRule>();
            manifest.rockRules ??= new List<ObjectSpawnRule>();

            int newLayers = ScanTextures(manifest, biomeDir + "/Textures", layerDir);

            int newProps = ScanRules(biomeDir + "/Props", manifest.spawnRules, go => new ObjectSpawnRule
            {
                label = go.name, prefabs = new[] { go }, count = 40,
                where = PlacementCondition.Range(0.12f, 0.9f, 0f, 28f),
                scaleRange = new Vector2(0.85f, 1.25f), randomYRotation = true
            });

            int newTrees = ScanRules(biomeDir + "/Trees", manifest.treeRules, go => new ObjectSpawnRule
            {
                label = go.name, prefabs = new[] { go }, count = 150,
                where = PlacementCondition.Range(0.1f, 0.85f, 0f, 25f),   // trees on gentle ground
                scaleRange = new Vector2(0.8f, 1.3f), randomYRotation = true
            });

            int newRocks = ScanRules(biomeDir + "/Rocks", manifest.rockRules, go => new ObjectSpawnRule
            {
                label = go.name, prefabs = new[] { go }, count = 40,
                where = PlacementCondition.Range(0.1f, 0.95f, 12f, 55f),  // rocks gravitate to slopes
                scaleRange = new Vector2(0.6f, 1.5f), randomYRotation = true,
                alignToNormal = true, nonUniformScale = true
            });

            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[BiomeScanner] '{manifest.biomeName}': +{newLayers} layer(s), +{newProps} prop(s), " +
                      $"+{newTrees} tree(s), +{newRocks} rock(s).", manifest);
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }

        // ---- Textures -> condition-driven layers --------------------------

        static int ScanTextures(BiomeAssetManifest manifest, string texDir, string layerDir)
        {
            if (!AssetDatabase.IsValidFolder(texDir))
            {
                Debug.LogWarning($"[BiomeScanner] No 'Textures' folder at {texDir}.");
                return 0;
            }
            if (!AssetDatabase.IsValidFolder(layerDir))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(layerDir).Replace("\\", "/"), "Layers");

            manifest.textureLayers ??= new List<BiomeTextureLayer>();
            var known = new HashSet<TerrainLayer>(manifest.textureLayers.Where(l => l != null).Select(l => l.terrainLayer));

            int added = 0;
            var texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { texDir });
            foreach (var guid in texGuids.OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
            {
                string texPath = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null) continue;

                // Ensure a reusable TerrainLayer asset exists for this texture.
                string layerPath = layerDir + "/" + Path.GetFileNameWithoutExtension(texPath) + ".terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                if (layer == null)
                {
                    layer = new TerrainLayer();
                    AssetDatabase.CreateAsset(layer, layerPath);
                }
                layer.diffuseTexture = tex;
                if (layer.tileSize == Vector2.zero) layer.tileSize = new Vector2(15f, 15f);
                EditorUtility.SetDirty(layer);

                if (known.Contains(layer)) continue; // already wired — keep its tuned condition

                manifest.textureLayers.Add(new BiomeTextureLayer
                {
                    label = Path.GetFileNameWithoutExtension(texPath),
                    terrainLayer = layer,
                    where = PlacementCondition.Everywhere, // tuned below for the whole set
                    weight = 1f
                });
                known.Add(layer);
                added++;
            }

            if (added > 0) AssignDefaultBands(manifest.textureLayers);
            return added;
        }

        /// <summary>
        /// Spreads layers low->high by author order, reserving the LAST as the cliff (steep, any height).
        /// Only applied when new layers were added, and never overwrites a layer whose condition was
        /// already changed away from "Everywhere".
        /// </summary>
        static void AssignDefaultBands(List<BiomeTextureLayer> layers)
        {
            int count = layers.Count;
            if (count == 0) return;
            if (count == 1)
            {
                if (IsEverywhere(layers[0].where)) layers[0].where = PlacementCondition.Range(0f, 1f, 0f, 90f);
                return;
            }

            int bands = count - 1; // last = cliff
            float step = 1f / bands;
            for (int i = 0; i < bands; i++)
            {
                if (!IsEverywhere(layers[i].where)) continue; // respect manual tuning
                float minH = Mathf.Clamp01(i * step - 0.1f);
                float maxH = Mathf.Clamp01((i + 1) * step + 0.1f);
                layers[i].where = PlacementCondition.Range(minH, maxH, 0f, 40f);
            }
            var cliff = layers[count - 1];
            if (IsEverywhere(cliff.where))
            {
                cliff.where = PlacementCondition.Range(0f, 1f, 38f, 90f);
                cliff.weight = 1.5f;
            }
        }

        static bool IsEverywhere(PlacementCondition c)
            => c.minHeight <= 0f && c.maxHeight >= 1f && c.minSlope <= 0f && c.maxSlope >= 90f;

        // ---- Prefab folder -> spawn rules ---------------------------------

        /// <summary>
        /// Adds one rule per NEW prefab found in <paramref name="dir"/> (built by <paramref name="make"/>),
        /// skipping prefabs already referenced by an existing rule so tuning is preserved. Returns count added.
        /// </summary>
        static int ScanRules(string dir, List<ObjectSpawnRule> rules, System.Func<GameObject, ObjectSpawnRule> make)
        {
            if (!AssetDatabase.IsValidFolder(dir)) return 0;

            var referenced = new HashSet<GameObject>();
            foreach (var r in rules)
                if (r?.prefabs != null)
                    foreach (var p in r.prefabs)
                        if (p != null) referenced.Add(p);

            int added = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go == null || referenced.Contains(go)) continue;
                rules.Add(make(go));
                referenced.Add(go);
                added++;
            }
            return added;
        }
    }
}
#endif
