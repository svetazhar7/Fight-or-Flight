#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using System;
using UnityEditor.Overlays;

namespace Pinwheel.Poseidon
{
    [CustomEditor(typeof(SplineHandle))]
    public class SplineHandleInspector : Editor
    {
        private SplineHandle m_instance;

        private void OnEnable()
        {
            m_instance = target as SplineHandle;
            SceneView.duringSceneGui += DuringSceneGUI;
            SplineToolContext.endMoveOrScaleCallback += OnEndMoveOrScaleAnyAnchor;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGUI;
            SplineToolContext.endMoveOrScaleCallback -= OnEndMoveOrScaleAnyAnchor;
        }

        private void DuringSceneGUI(SceneView sv)
        {
            if (ToolManager.activeToolType != typeof(AddRemoveSplineAnchorsTool))
            {
                m_instance.ValidateAnchors();
                SplineEditorUtilities.color = Color.cyan;
                SplineEditorUtilities.DrawSplinePath(m_instance);
                SplineEditorUtilities.DrawSplineAnchors(m_instance);
            }

            RiverWater river = m_instance.GetComponentInParent<RiverWater>();
            if (river != null)
            {
                foreach (SplineHandle spline in river.splines)
                {
                    if (spline != null && spline != m_instance && spline.isActiveAndEnabled)
                    {
                        spline.ValidateAnchors();
                        SplineEditorUtilities.color = new Color(1, 1, 1, 0.25f);
                        SplineEditorUtilities.DrawSplinePath(spline);
                        SplineEditorUtilities.DrawSplineAnchors(spline);
                    }
                }
            }
        }

        private void OnEndMoveOrScaleAnyAnchor(SplineHandle spline)
        {
            RiverWater river = m_instance.GetComponentInParent<RiverWater>();
            if (river != null)
            {
                river.GenerateMesh();
            }
        }

        [Overlay(typeof(SceneView), "Spline Utilities")]
        private class SplineHandleUtilitiesOverlay : IMGUIOverlay, ITransientOverlay
        {
            public bool visible
            {
                get
                {
                    return
                        ToolManager.activeContextType == typeof(GameObjectToolContext) &&
                        Selection.activeGameObject != null &&
                        Selection.activeGameObject.GetComponent<SplineHandle>() != null;
                }
            }

            public override void OnGUI()
            {
                if (GUILayout.Button("Centerize Pivot"))
                {
                    if (Selection.activeGameObject != null)
                    {
                        SplineHandle spline = Selection.activeGameObject.GetComponent<SplineHandle>();
                        if (spline != null)
                        {
                            Undo.RecordObject(spline.transform, "Centerize spline pivot");
                            Undo.RecordObject(spline, "Centerize spline pivot");
                            EditorUtility.SetDirty(spline.gameObject);
                            spline.CenterizePivotPoint();
                        }
                    }
                }
            }
        }
    }
}

#endif