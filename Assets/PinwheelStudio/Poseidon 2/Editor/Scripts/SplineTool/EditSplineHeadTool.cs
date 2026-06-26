#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.EditorTools;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace Pinwheel.Poseidon
{
    [EditorTool("Edit Spline Head", typeof(SplineHandle))]
    public class EditSplineHeadTool : EditorTool
    {
        protected GUIContent m_toolbarIcon;
        public override GUIContent toolbarIcon
        {
            get
            {
                return m_toolbarIcon;
            }
        }

        protected List<Transform> m_headCandidates = new List<Transform>();

        private void OnEnable()
        {
            m_toolbarIcon = new GUIContent(Resources.Load<Texture2D>("Poseidon/Textures/EditSplineHeadIcon"), "Edit spline head");

            m_headCandidates = new List<Transform>();
            SplineHandle targetSpline = target as SplineHandle;
            SplineHandle[] splines = FindObjectsByType<SplineHandle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (SplineHandle spline in splines)
            {
                if (spline == targetSpline)
                    continue;
                spline.ValidateAnchors();
                m_headCandidates.AddRange(spline.anchors);
            }
        }

        private void OnDisable()
        {
            if (m_toolbarIcon != null && m_toolbarIcon.image != null)
            {
                Resources.UnloadAsset(m_toolbarIcon.image);
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            if (!(target is SplineHandle spline))
                return;

            if (spline.anchors.Count == 0)
                return;


            foreach (Transform candid in m_headCandidates)
            {
                if (candid != spline.head)
                {
                    Handles.color = Color.yellow;
                    if (Handles.Button(candid.position, Quaternion.Euler(-90, 0, 0), 1, 1, Handles.CircleHandleCap))
                    {
                        Undo.RecordObject(spline, "Set spline head");
                        spline.head = candid;
                        EditorUtility.SetDirty(spline);
                    }
                }
                else
                {
                    Handles.color = Color.red;
                    if (Handles.Button(candid.position, Quaternion.Euler(-90, 0, 0), 1, 1, Handles.CircleHandleCap))
                    {
                        Undo.RecordObject(spline, "Remove spline head");
                        spline.head = null;
                        EditorUtility.SetDirty(spline);
                    }
                }
            }
        }
    }

    [Overlay(typeof(SceneView), "Edit spline's head")]
    public class EditSplineHeadOverlay : Overlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(EditSplineHeadTool);

        public override VisualElement CreatePanelContent()
        {
            IMGUIContainer imgui = new IMGUIContainer() { onGUIHandler = OnGUI };
            imgui.style.width = new StyleLength(new Length(300, LengthUnit.Pixel));
            return imgui;
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("Click on a yellow circle to connect spline's head to.");
            EditorGUILayout.LabelField("Click on the red circle to disconnect spline's head.");
        }
    }
}

#endif