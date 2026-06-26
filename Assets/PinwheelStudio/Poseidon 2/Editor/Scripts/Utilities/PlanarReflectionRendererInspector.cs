#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using Pinwheel.Poseidon.JupiterPreview;
using System;

namespace Pinwheel.Poseidon
{
    [CustomEditor(typeof(PlanarReflectionRenderer))]
    public class PlanarReflectionRendererInspector : Editor
    {
        public static event Func<bool> IsJupiterInstalled;
        public static event Func<string> GetJupiterVersionString;
        public static event Func<GameObject> GetJupiterSkyGameObjectInScene;
        public static event Func<GameObject> AddJupiterSky;
        public static event Action<GameObject, JupiterSkyPreview> MatchJupiterSettingsWithPreview;

        private PlanarReflectionRenderer m_instance;

        private void OnEnable()
        {
            m_instance = target as PlanarReflectionRenderer;
        }

        public override void OnInspectorGUI()
        {
            DrawGUI(m_instance);
        }

        public static void DrawGUI(PlanarReflectionRenderer instance)
        {
            CheckMaterialHasReflectionEffect(instance);
            DrawRenderingGUI(instance);
            EditorGUILayout.Space();
            DrawSkyGUI();
        }

        private class RenderingGUI
        {
            public static readonly GUIContent HEADER_RENDERING = new GUIContent("Rendering");
            public static readonly GUIContent RESOLUTION = new GUIContent("Resolution");
            public static readonly GUIContent CLIP_PLANE_OFFSET = new GUIContent("Clip Plane Offset");
            public static readonly GUIContent LAYERS = new GUIContent("Layers", "Object in these layers will be rendered again for reflection");
            public static readonly GUIContent RENDERER_INDEX = new GUIContent("Renderer Index", "Index of the URP Renderer Asset. Reflection should be rendered with a minimal renderer (no post effects) for performance");
        }

        private static void DrawRenderingGUI(PlanarReflectionRenderer instance)
        {
            EditorGUILayout.LabelField(RenderingGUI.HEADER_RENDERING, EditorStyles.boldLabel);

            EditorGUILayout.HelpBox("Planar reflection only works in Play Mode for the Main Camera.\nStereo rendering (VR) not supported.", MessageType.Info, true);
            EditorGUI.BeginChangeCheck();
            int resolution = EditorGUILayout.DelayedIntField(RenderingGUI.RESOLUTION, instance.textureResolution);
            float clipPlaneOffset = EditorGUILayout.FloatField(RenderingGUI.CLIP_PLANE_OFFSET, instance.clipPlaneOffset);
            int rendererIndex = instance.rendererIndex;
            if (PCommon.CurrentRenderPipeline == RenderPipelineType.Universal)
            {
                rendererIndex = EditorGUILayout.DelayedIntField(RenderingGUI.RENDERER_INDEX, instance.rendererIndex);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(instance, $"Editing {instance.name}");
                EditorUtility.SetDirty(instance);
                instance.textureResolution = resolution;
                instance.clipPlaneOffset = clipPlaneOffset;
                instance.rendererIndex = rendererIndex;
            }

            SerializedObject so = new SerializedObject(instance);
            SerializedProperty reflectionLayerProps = so.FindProperty("m_reflectionLayers");
            if (reflectionLayerProps != null)
            {
                EditorGUILayout.PropertyField(reflectionLayerProps);
            }
            so.ApplyModifiedProperties();
            reflectionLayerProps.Dispose();
            so.Dispose();
        }

        private static void DrawSkyGUI()
        {
            JupiterSkyPreview jupiterPreview =
#if UNITY_6000_0_OR_NEWER
                FindFirstObjectByType<JupiterSkyPreview>();
#else
                FindObjectOfType<JupiterSkyPreview>();
#endif
            EditorGUILayout.LabelField("Sky (Reflection source)", EditorStyles.boldLabel);
            if (!IsJupiterInstalled())
            {
                if (jupiterPreview == null)
                {
                    EditorGUILayout.LabelField("The skybox contributes to water reflection. A lightweight animated sky is provided to help evaluate water appearance under different lighting scenarios.", PEditorCommon.WordWrapItalicLabel);
                    if (GUILayout.Button("Add Preview Sky"))
                    {
                        NetUtils.TrackClick("add_preview_sky", UILocation.Inspector);

                        GameObject g = new GameObject("Day Sky - Jupiter Preview");
                        JupiterSkyPreview skyComponent = g.AddComponent<JupiterSkyPreview>();
                        skyComponent.ApplyPresetBasedOnDominantLight();
                        Selection.activeObject = skyComponent;
                    }
                }
                else
                {
                    GUI.enabled = false;
                    EditorGUILayout.ObjectField("Sky", jupiterPreview, typeof(JupiterSkyPreview), true);
                    GUI.enabled = true;
                }
            }
            else
            {
                EditorGUILayout.LabelField($"Jupiter {GetJupiterVersionString()} was installed.", PEditorCommon.ItalicLabel);
                GameObject jupiterSkyObject = GetJupiterSkyGameObjectInScene();
                if (jupiterSkyObject == null && jupiterPreview == null)
                {
                    EditorGUILayout.LabelField($"Current scene doesn't contain Jupiter sky object.", PEditorCommon.ItalicLabel);
                    if (GUILayout.Button("Add Jupiter sky"))
                    {
                        jupiterSkyObject = AddJupiterSky();
                    }
                }
                else if (jupiterSkyObject == null && jupiterPreview != null && jupiterPreview.isActiveAndEnabled)
                {
                    EditorGUILayout.LabelField($"Current scene doesn't contain Jupiter sky object, but a preview is in used.", PEditorCommon.WordWrapItalicLabel);
                    if (GUILayout.Button("Add Jupiter sky & disable preview"))
                    {
                        jupiterPreview.gameObject.SetActive(false);
                        jupiterSkyObject = AddJupiterSky();
                        MatchJupiterSettingsWithPreview(jupiterSkyObject, jupiterPreview);
                    }
                }
                else if (jupiterSkyObject != null && jupiterPreview != null && jupiterPreview.isActiveAndEnabled)
                {
                    EditorGUILayout.LabelField($"Jupiter sky and its preview should not be active at the same time in the scene.", PEditorCommon.WordWrapItalicLabel);
                    if (GUILayout.Button("Disable preview"))
                    {
                        jupiterPreview.gameObject.SetActive(false);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Use the following object to configure your sky.", PEditorCommon.WordWrapItalicLabel);
                    EditorGUILayout.ObjectField("Sky", jupiterSkyObject, typeof(GameObject), true);
                }
            }
        }

        private static void CheckMaterialHasReflectionEffect(PlanarReflectionRenderer instance)
        {
            if (instance.planarWaterBody != null)
            {
                if (instance.planarWaterBody.material != null)
                {
                    bool hasReflectionEffect = instance.planarWaterBody.material.HasTexture(PMat.REFLECTION_TEX);
                    if (!hasReflectionEffect)
                    {
                        EditorGUILayout.HelpBox($"Current water material doesn't have property \"{PMat.REFLECTION_TEX_STR}\". Consider disabling this component.", MessageType.Warning);
                    }
                }
            }
        }
    }
}

#endif