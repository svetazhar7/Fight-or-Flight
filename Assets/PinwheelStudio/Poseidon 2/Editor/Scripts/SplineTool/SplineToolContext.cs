#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.EditorTools;
using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace Pinwheel.Poseidon
{
    [EditorToolContext("River/Spline Tool Context", typeof(SplineHandle))]
    public class SplineToolContext : EditorToolContext
    {
        public static event Action<SplineHandle, Transform> movingAnchorCallback;
        public static event Action<SplineHandle, Transform> scalingAnchorCallback;

        public static event Action<SplineHandle> endMoveOrScaleCallback;

        public static Transform lastEditedAnchor;

        public override void OnWillBeDeactivated()
        {
            base.OnWillBeDeactivated();
            lastEditedAnchor = null;
        }

        protected override Type GetEditorToolType(Tool tool)
        {
            switch (tool)
            {
                case Tool.Move: return typeof(MoveSplineAnchorTool);
                case Tool.Rotate: return null;
                case Tool.Scale: return null;

                default: return null;
            }
        }

        private static void DrawLastEditedAnchorHighlight()
        {
            if (lastEditedAnchor != null)
            {
                Handles.color = Handles.selectedColor;
                Handles.DrawWireDisc(lastEditedAnchor.position, Vector3.up, 1);
            }
        }

        public class MoveSplineAnchorTool : EditorTool
        {
            public override void OnToolGUI(EditorWindow window)
            {
                foreach (var obj in targets)
                {
                    if (!(obj is GameObject selectedGO))
                        continue;
                    SplineHandle spline = selectedGO.GetComponent<SplineHandle>();
                    if (spline == null)
                        continue;

                    //PositionHandle() will eat mouse up event, so check it here
                    //On mouse up, PositionHandle() won't mark GUI as changed.
                    bool isMouseUp = Event.current.type == EventType.MouseUp;
                    if (isMouseUp)
                    {
                        endMoveOrScaleCallback?.Invoke(spline);
                    }

                    spline.ValidateAnchors();
                    foreach (Transform a in spline.anchors)
                    {
                        EditorGUI.BeginChangeCheck();
                        a.position = Handles.PositionHandle(a.position, Tools.handleRotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorUtility.SetDirty(a);
                            movingAnchorCallback?.Invoke(spline, a);
                            lastEditedAnchor = a;
                        }
                    }
                }

                DrawLastEditedAnchorHighlight();
            }
        }

        public class ScaleSplineAnchorTool : EditorTool
        {
            public override void OnToolGUI(EditorWindow window)
            {
                foreach (var obj in targets)
                {
                    if (!(obj is GameObject selectedGO))
                        continue;
                    SplineHandle spline = selectedGO.GetComponent<SplineHandle>();
                    if (spline == null)
                        continue;


                    //PositionHandle() will eat mouse up event, so check it here
                    //On mouse up, PositionHandle() won't mark GUI as changed.
                    bool isMouseUp = Event.current.type == EventType.MouseUp;
                    if (isMouseUp)
                    {
                        endMoveOrScaleCallback?.Invoke(spline);
                    }

                    spline.ValidateAnchors();
                    foreach (Transform a in spline.anchors)
                    {
                        EditorGUI.BeginChangeCheck();
                        a.localScale = Handles.ScaleHandle(a.localScale, a.position, Tools.handleRotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorUtility.SetDirty(a);
                            scalingAnchorCallback?.Invoke(spline, a);
                            lastEditedAnchor = a;
                        }
                    }
                }

                DrawLastEditedAnchorHighlight();
            }
        }

        [Overlay(typeof(SceneView), "Anchor transform")]
        public class LastEditedAnchorTransformOverlay : Overlay, ITransientOverlay
        {
            public bool visible => SplineToolContext.lastEditedAnchor != null;

            public override VisualElement CreatePanelContent()
            {
                IMGUIContainer imgui = new IMGUIContainer() { onGUIHandler = OnGUI };
                imgui.style.width = new StyleLength(new Length(300, LengthUnit.Pixel));
                return imgui;
            }

            public void OnGUI()
            {
                if (lastEditedAnchor != null)
                {
                    EditorGUI.BeginChangeCheck();

                    float labelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 100;
                    EditorGUIUtility.wideMode = true;
                    Vector3 localPosition = EditorGUILayout.Vector3Field("Position", SplineToolContext.lastEditedAnchor.localPosition);
                    //Vector3 localScale = EditorGUILayout.Vector3Field("Scale", SplineToolContext.lastEditedAnchor.localScale);
                    EditorGUIUtility.wideMode = false;
                    EditorGUIUtility.labelWidth = labelWidth;

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(SplineToolContext.lastEditedAnchor, "Editing anchor");
                        SplineToolContext.lastEditedAnchor.localPosition = localPosition;
                        //SplineToolContext.lastEditedAnchor.localScale = localScale;
                    }
                }
            }
        }
    }


}

#endif