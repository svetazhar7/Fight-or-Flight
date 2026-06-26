#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Analytics;
using System;
using Pinwheel.Poseidon;

namespace Pinwheel.Poseidon.JupiterPreview
{
    [CustomEditor(typeof(JupiterSkyPreview))]
    public class JupiterSkyPreviewInspector : Editor
    {
        public static event Func<bool> IsJupiterInstalled;
        public static event Func<string> GetJupiterVersionString;
        public static event Func<GameObject> GetJupiterSkyGameObjectInScene;
        public static event Func<GameObject> AddJupiterSky;
        public static event Action<GameObject, JupiterSkyPreview> MatchJupiterSettingsWithPreview;

        [SerializeField]
        private Texture2D jupiterBackground;

        private static GUIStyle bannerNoteLabel;
        private static GUIStyle BannerNoteLabel
        {
            get
            {
                if (bannerNoteLabel == null)
                {
                    bannerNoteLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                }
                bannerNoteLabel.alignment = TextAnchor.LowerRight;
                bannerNoteLabel.fontStyle = FontStyle.Italic;
                return bannerNoteLabel;
            }
        }

        private JupiterSkyPreview instance;
        private void OnEnable()
        {
            instance = target as JupiterSkyPreview;
        }

        public override void OnInspectorGUI()
        {
            if (jupiterBackground != null)
            {
                Rect backgroundRect = EditorGUILayout.GetControlRect(GUILayout.Height(90));
                GUI.DrawTexture(backgroundRect, jupiterBackground, ScaleMode.ScaleAndCrop);

                GUI.Label(backgroundRect, "Starry night sky made with Jupiter", BannerNoteLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Apply preset", EditorStyles.boldLabel, GUILayout.Width(EditorGUIUtility.labelWidth));
            if (GUILayout.Button("Day sky", EditorStyles.miniButtonLeft))
            {
                Undo.RecordObject(instance, "Apply sky preset");
                instance.ApplyDayPreset();
            }
            if (GUILayout.Button("Night sky", EditorStyles.miniButtonRight))
            {
                Undo.RecordObject(instance, "Apply sky preset");
                instance.ApplyNightPreset();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sky Gradient", EditorStyles.boldLabel);
            instance.SkyColor = EditorGUILayout.ColorField(EditorGUIUtility.TrTextContent("Sky Color"), instance.SkyColor, true, true, true);
            instance.HorizonColor = EditorGUILayout.ColorField(EditorGUIUtility.TrTextContent("Horizon Color"), instance.HorizonColor, true, true, true);
            instance.GroundColor = EditorGUILayout.ColorField(EditorGUIUtility.TrTextContent("Ground Color"), instance.GroundColor, true, true, true);
            instance.HorizonThickness = EditorGUILayout.Slider("Horizon Thickness", instance.HorizonThickness, 0f, 1f);
            instance.HorizonExponent = EditorGUILayout.FloatField("Horizon Exponent", instance.HorizonExponent);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sun", EditorStyles.boldLabel);
            instance.EnableSun = EditorGUILayout.Toggle("Enable", instance.EnableSun);
            if (instance.EnableSun)
            {
                instance.SunColor = EditorGUILayout.ColorField(EditorGUIUtility.TrTextContent("Color"), instance.SunColor, true, true, true);
                instance.SunSize = EditorGUILayout.Slider("Size", instance.SunSize, 0f, 1f);
                instance.SunSoftEdge = EditorGUILayout.Slider("Soft Edge", instance.SunSoftEdge, 0f, 1f);
                instance.SunGlow = EditorGUILayout.Slider("Glow", instance.SunGlow, 0f, 1f);

                instance.SunLightSource = EditorGUILayout.ObjectField("Light Source", instance.SunLightSource, typeof(Light), true) as Light;
                instance.SunLightColor = EditorGUILayout.ColorField(EditorGUIUtility.TrTextContent("Light Color"), instance.SunLightColor, true, false, false);
                instance.SunLightIntensity = EditorGUILayout.FloatField("Light Intensity", instance.SunLightIntensity);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Cloud", EditorStyles.boldLabel);
            instance.EnableOverheadCloud = EditorGUILayout.Toggle("Enable", instance.EnableOverheadCloud);

            EditorGUILayout.Space();

            if (!IsJupiterInstalled())
            {
                EditorGUILayout.LabelField("This preview shows a simplified sky setup to demonstrate how Poseidon water behaves under an animated sky, particularly with reflections.", JEditorCommon.WordWrapItalicLabel);
                EditorGUILayout.LabelField("Jupiter adds more sky features such as moon and stars, horizon clouds, detail overlays, and deeper artistic, animation, and performance control.", JEditorCommon.WordWrapItalicLabel);
                if (EditorGUILayout.LinkButton("Learn more about Jupiter →"))
                {
                    NetUtils.TrackClick("learn_more_jupiter", UILocation.Inspector);
                    Application.OpenURL("https://assetstore.unity.com/packages/2d/textures-materials/sky/procedural-sky-shader-day-night-cycle-jupiter-159992");
                }
            }
            else
            {
                EditorGUILayout.LabelField($"Jupiter {GetJupiterVersionString()} was installed.", JEditorCommon.ItalicLabel);
                GameObject jupiterSkyObject = GetJupiterSkyGameObjectInScene();
                if (jupiterSkyObject == null)
                {
                    EditorGUILayout.LabelField($"Current scene doesn't contain Jupiter sky object.", JEditorCommon.ItalicLabel);
                    if (GUILayout.Button("Add Jupiter sky"))
                    {
                        jupiterSkyObject = AddJupiterSky();
                        MatchJupiterSettingsWithPreview(jupiterSkyObject, instance);
                    }
                }
                else if (jupiterSkyObject == null && instance.isActiveAndEnabled)
                {
                    EditorGUILayout.LabelField($"Current scene doesn't contain Jupiter sky object, but this preview is in used.", JEditorCommon.WordWrapItalicLabel);
                    if (GUILayout.Button("Add Jupiter sky & disable preview"))
                    {
                        instance.gameObject.SetActive(false);
                        jupiterSkyObject = AddJupiterSky();
                        MatchJupiterSettingsWithPreview(jupiterSkyObject, instance);
                    }
                }
                else if (jupiterSkyObject != null && instance.isActiveAndEnabled)
                {
                    EditorGUILayout.LabelField($"Jupiter sky and its preview should not be active at the same time in the scene.", JEditorCommon.WordWrapItalicLabel);
                    if (GUILayout.Button("Disable preview"))
                    {
                        instance.gameObject.SetActive(false);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Use the following object to configure your sky.", JEditorCommon.WordWrapItalicLabel);
                    EditorGUILayout.ObjectField("Sky", jupiterSkyObject, typeof(GameObject), true);
                }
            }
        }
    }
}

#endif