#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using System.Linq;
using UnityEditor.Overlays;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.IO;
using UnityEditor.Presets;

namespace Pinwheel.Poseidon
{
    public class WaterBodyInspectorBase : Editor
    {
        private PoseidonWaterBody m_waterBody;
        private Editor m_materialEditor;

        protected virtual bool shouldDrawMaterialEditor => true;

        protected virtual void OnEnable()
        {
            m_waterBody = target as PoseidonWaterBody;
        }

        protected virtual void OnDisable()
        {
        }

        public override void OnInspectorGUI()
        {
            DrawRenderingSettings();
            DrawMeshGenSettings();
            DrawTimeSettings();

            DrawWaveMaskHelper();
            DrawPlanarReflectionHelper();

            EditorGUILayout.Space();
            PEditorCommon.DrawCommonLinks();
            EditorGUILayout.Space();

            if (shouldDrawMaterialEditor && m_waterBody.material != null)
            {
                MaterialEditor.CreateCachedEditor(m_waterBody.material, typeof(MaterialEditor), ref m_materialEditor);
                MaterialEditor matEditor = m_materialEditor as MaterialEditor;
                matEditor.DrawHeader();
                if (matEditor.isVisible)
                {
                    matEditor.OnInspectorGUI();
                }
            }
        }

        class RenderingGUI
        {
            public static string ID = "poseidon-water-rendering";
            public static string HEADER = "Rendering";

            public static readonly GUIContent MATERIAL = new GUIContent("Material", "The material for rendering this water body");
        }

        private void DrawRenderingSettings()
        {
            PEditorCommon.Foldout(RenderingGUI.HEADER, true, RenderingGUI.ID, () =>
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                Material mat = EditorGUILayout.ObjectField(RenderingGUI.MATERIAL, m_waterBody.material, typeof(Material), false) as Material;

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(m_waterBody, $"Modify {m_waterBody.name}");
                    m_waterBody.material = mat;
                }
                if (m_waterBody.material != null)
                {
                    if (GUILayout.Button("Select Shader", EditorStyles.miniButton, GUILayout.Width(100)))
                    {
                        WaterBrowserWindow.Show(m_waterBody.material);
                    }
                }
                else
                {
                    if (GUILayout.Button("Create", EditorStyles.miniButton, GUILayout.Width(100)))
                    {
                        CreateNewWaterMaterial();
                    }
                }

                EditorGUILayout.EndHorizontal();

            });
        }

        protected void CreateNewWaterMaterial()
        {
            Scene scene = m_waterBody.gameObject.scene;
            string sceneDirectory = Path.GetDirectoryName(scene.path);
            string sceneDataDirectory = Path.Combine(sceneDirectory, scene.name);
            if (!Directory.Exists(sceneDataDirectory))
            {
                Directory.CreateDirectory(sceneDataDirectory);
            }

            Material materialDefault = PEditorCommon.FindFirstAssetWithLabel<Material>("PoseidonDefaultMaterial");
            if (materialDefault != null)
            {
                Material mat = Object.Instantiate<Material>(materialDefault);
                mat.name = $"WaterMaterial_{m_waterBody.name}_{Random.Range(0, 9999)}";
                string fileName = $"{mat.name}.mat";
                AssetDatabase.CreateAsset(mat, Path.Combine(sceneDataDirectory, fileName));
                AssetDatabase.Refresh();

                m_waterBody.material = mat;
                EditorUtility.SetDirty(m_waterBody);

                EditorGUIUtility.PingObject(mat);
            }
        }

        class MeshGenGUI
        {
            public static string ID = "poseidon-water-meshgen";
            public static string HEADER = "Mesh Generation";
        }

        private void DrawMeshGenSettings()
        {
            PEditorCommon.Foldout(MeshGenGUI.HEADER, true, MeshGenGUI.ID, () =>
            {
                OnMeshGenGUI();
            });
        }

        protected virtual void OnMeshGenGUI()
        {

        }

        class TimeGUI
        {
            public static string ID = "tileable-water-time";
            public static string HEADER = "Time";

            public static readonly GUIContent TIME_MODE = new GUIContent("Time Mode", "Use Auto to let the component animate freely, use Manual to control water animation & timing on your own (multiplayer game).");
            public static readonly GUIContent TIME = new GUIContent("Time", "Current Time value");
        }

        private void DrawTimeSettings()
        {
            PEditorCommon.Foldout(TimeGUI.HEADER, true, TimeGUI.ID, () =>
            {
                EditorGUI.BeginChangeCheck();
                TimeMode timeMode = (TimeMode)EditorGUILayout.EnumPopup(TimeGUI.TIME_MODE, m_waterBody.timeMode);
                float manualTime = m_waterBody.manualTimeSeconds;
                if (timeMode == TimeMode.Auto)
                {
                    GUI.enabled = false;
                    EditorGUILayout.FloatField(TimeGUI.TIME, m_waterBody.GetTimeParam());
                    GUI.enabled = true;
                }
                else
                {
                    manualTime = EditorGUILayout.FloatField(TimeGUI.TIME, m_waterBody.manualTimeSeconds);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(m_waterBody, $"Modify {m_waterBody.name}");
                    m_waterBody.timeMode = timeMode;
                    m_waterBody.manualTimeSeconds = manualTime;
                }
            });
        }

        class PRGUI
        {
            public static string ID = "poseidon-water-PR";
            public static string HEADER = "Planar Reflection";
        }

        protected void DrawPlanarReflectionHelper()
        {
            if (m_waterBody.material != null)
            {
                bool hasReflectionEffect = PMat.HasReflectionFX(m_waterBody.material);
                if (hasReflectionEffect)
                {
                    PEditorCommon.Foldout(PRGUI.HEADER, true, PRGUI.ID, () =>
                    {
                        PlanarReflectionRenderer reflectionRenderer = m_waterBody.GetComponent<PlanarReflectionRenderer>();
                        if (reflectionRenderer == null)
                        {
                            EditorGUILayout.LabelField($"You need to add a {nameof(PlanarReflectionRenderer)} component for rendering reflection texture.");
                            if (GUILayout.Button("+Add"))
                            {
                                Undo.AddComponent<PlanarReflectionRenderer>(m_waterBody.gameObject);
                                EditorUtility.SetDirty(m_waterBody.gameObject);
                            }
                        }
                        else
                        {
                            PlanarReflectionRendererInspector.DrawGUI(reflectionRenderer);
                        }
                    });
                }
            }
        }

        class WMGUI
        {
            public static string ID = "poseidon-water-WM";
            public static string HEADER = "Wave Mask";
        }

        private static Editor waveMaskControllerEditor;

        protected void DrawWaveMaskHelper()
        {
            if (m_waterBody.material != null)
            {
                bool hasWaveMaskEffect = PMat.HasWaveMaskFX(m_waterBody.material);
                if (hasWaveMaskEffect)
                {
                    PEditorCommon.Foldout(WMGUI.HEADER, true, WMGUI.ID, () =>
                    {
                        WaveMaskController controller = m_waterBody.GetComponent<WaveMaskController>();
                        if (controller == null)
                        {
                            EditorGUILayout.LabelField($"You need to add a {nameof(WaveMaskController)} component for controlling world space wave mask.");
                            if (GUILayout.Button("+Add"))
                            {
                                Undo.AddComponent<WaveMaskController>(m_waterBody.gameObject);
                                EditorUtility.SetDirty(m_waterBody.gameObject);
                            }
                        }
                        else
                        {
                            Editor.CreateCachedEditor(controller, typeof(WaveMaskControllerInspector), ref waveMaskControllerEditor);
                            waveMaskControllerEditor.OnInspectorGUI();
                        }
                    });
                }
            }
        }
    }
}

#endif