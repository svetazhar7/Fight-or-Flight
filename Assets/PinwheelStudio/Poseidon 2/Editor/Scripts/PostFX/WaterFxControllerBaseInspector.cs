#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace Pinwheel.Poseidon.FX
{
    [CustomEditor(typeof(WaterFxControllerBase))]
    public abstract class WaterFxControllerBaseInspector : Editor
    {
        protected WaterFxControllerBase m_instance;
        protected SerializedProperty m_onEnterWaterProperty;
        protected SerializedProperty m_onExitWaterProperty;

        protected virtual void OnEnable()
        {
            m_instance = target as WaterFxControllerBase;
            m_onEnterWaterProperty = serializedObject.FindProperty("m_onEnterWater");
            m_onExitWaterProperty = serializedObject.FindProperty("m_onExitWater");
        }

        protected virtual void OnDisable()
        {

        }

        public override void OnInspectorGUI()
        {
            DrawConfigurationCheck();
            DrawVolumesSettings();
            DrawWetLensSettings();
            DrawEvents();
        }

        protected abstract void DrawConfigurationCheck();

        protected class VolumeGUI
        {
            public static string ID = "waterfx-volumes";
            public static string TITLE = "Volumes";

            public static readonly GUIContent VOLUME_EXTENT = new GUIContent("Extent", "Extend volumes dimension on setup");
            public static readonly GUIContent VOLUME_LAYER = new GUIContent("Layer", "Layer of the volume game object");

        }

        protected void DrawVolumesSettings()
        {
            PEditorCommon.Foldout(VolumeGUI.TITLE, true, VolumeGUI.ID, () =>
            {
                EditorGUI.BeginChangeCheck();
                Vector3 extent = EditorGUILayout.Vector3Field(VolumeGUI.VOLUME_EXTENT, m_instance.volumeExtent);
                int layer = EditorGUILayout.LayerField(VolumeGUI.VOLUME_LAYER, m_instance.volumeLayer);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(m_instance, $"Modify {m_instance.name}");
                    EditorUtility.SetDirty(m_instance);
                    m_instance.volumeExtent = extent;
                    m_instance.volumeLayer = layer;
                    m_instance.SetupVolumes();
                }

                DrawVolumeProfileField();
            });
        }

        protected abstract void DrawVolumeProfileField();

        protected class WetLensGUI
        {
            public static string ID = "waterfx-wetlens";
            public static string TITLE = "Wet Lens";

            public static GUIContent DURATION = new GUIContent("Duration", "How long the wet lens will last since its start?");
            public static GUIContent FADE_CURVE = new GUIContent("Fade Curve", "Define the pace of fading out.");
        }

        protected void DrawWetLensSettings()
        {
            PEditorCommon.Foldout(WetLensGUI.TITLE, true, WetLensGUI.ID, () =>
            {
                EditorGUI.BeginChangeCheck();
                float duration = EditorGUILayout.FloatField(WetLensGUI.DURATION, m_instance.wetLensDuration);
                AnimationCurve fadeCurve = EditorGUILayout.CurveField(WetLensGUI.FADE_CURVE, m_instance.wetLensFadeOutCurve);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(m_instance, $"Modify {m_instance.name}");
                    EditorUtility.SetDirty(m_instance);
                    m_instance.wetLensDuration = duration;
                    m_instance.wetLensFadeOutCurve = fadeCurve;
                }
            });
        }

        protected class EventsGUI
        {
            public static string ID = "waterfx-events";
            public static string TITLE = "Events";

            public static GUIContent DURATION = new GUIContent("Duration", "How long the wet lens will last since its start?");
            public static GUIContent FADE_CURVE = new GUIContent("Fade Curve", "Define the pace of fading out.");
        }

        protected void DrawEvents()
        {
            PEditorCommon.Foldout(EventsGUI.TITLE, false, EventsGUI.ID, () =>
            {
                EditorGUILayout.PropertyField(m_onEnterWaterProperty);
                EditorGUILayout.PropertyField(m_onExitWaterProperty);
                serializedObject.ApplyModifiedProperties();
            });
        }
    }
}

#endif