#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace IslandSystem.EditorTools
{
    /// <summary>Inspector for <see cref="IslandTypeDefinition"/>: surfaces the composition % total and
    /// offers a one-click normalize so the biome shares always add up to 100.</summary>
    [CustomEditor(typeof(IslandTypeDefinition))]
    public class IslandTypeDefinitionEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var def = (IslandTypeDefinition)target;
            float total = def.TotalPercent;
            EditorGUILayout.Space();

            var type = Mathf.Abs(total - 100f) < 0.01f ? MessageType.Info : MessageType.Warning;
            EditorGUILayout.HelpBox($"Composition total: {total:0.#}% (should be 100%).", type);

            if (GUILayout.Button("Normalize to 100%"))
            {
                if (total > 0.0001f && def.biomes != null)
                {
                    Undo.RecordObject(def, "Normalize biome composition");
                    foreach (var b in def.biomes)
                        if (b != null && b.biome != null) b.percent = b.percent / total * 100f;
                    EditorUtility.SetDirty(def);
                    AssetDatabase.SaveAssets();
                }
            }
        }
    }
}
#endif
