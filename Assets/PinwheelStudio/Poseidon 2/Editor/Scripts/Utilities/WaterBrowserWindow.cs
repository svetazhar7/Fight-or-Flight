#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace Pinwheel.Poseidon
{
    public class WaterBrowserWindow : EditorWindow
    {
        protected Material m_targetMaterial;
        protected WaterShaderLibrary m_library;
        protected ShaderBrowserTreeView m_treeView;
        protected Vector2 m_scrollPos;

        public static void Show(Material target)
        {
            WaterBrowserWindow window = GetWindow<WaterBrowserWindow>();
            window.titleContent = new GUIContent("Water Shader Browser");
            window.m_targetMaterial = target;
            window.ShowUtility();
        }

        public void OnEnable()
        {
            LoadLibrary();
        }

        public void OnDisable()
        {
        }

        public void OnFocus()
        {
            LoadLibrary();
        }

        public void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shader Browser", Styles.H1);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Below is the set of pre-made water shaders with different feature combinations. Pick one for your material.");
            if (m_targetMaterial != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = false;
                EditorGUILayout.ObjectField(m_targetMaterial, typeof(Material), false);
                GUI.enabled = true;

                GUI.enabled = m_treeView.HasSelection();
                GUI.backgroundColor = GUI.enabled ? new Color(0.13f, 0.59f, 0.95f, 1) * 2.2f : Color.white;
                if (GUILayout.Button("Apply", EditorStyles.miniButton, GUILayout.Width(100)))
                {
                    ShaderDesc shaderDesc = m_treeView.GetSelectedShader();
                    m_targetMaterial.shader = shaderDesc.shader;
                }
                GUI.enabled = true;
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();

            m_scrollPos = EditorGUILayout.BeginScrollView(m_scrollPos);
            if (m_library != null)
            {
                Rect treeViewRect = EditorGUILayout.GetControlRect(GUILayout.Height(400));
                m_treeView.OnGUI(treeViewRect);
            }
            else
            {
                EditorGUILayout.LabelField("Shader library not found!");
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void LoadLibrary()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(WaterShaderLibrary).Name}");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                m_library = AssetDatabase.LoadAssetAtPath<WaterShaderLibrary>(path);
                m_treeView = ShaderBrowserTreeView.Create(m_library);
                m_treeView.Reload();
            }
            else
            {
                m_library = null;
            }
        }
    }
}

#endif