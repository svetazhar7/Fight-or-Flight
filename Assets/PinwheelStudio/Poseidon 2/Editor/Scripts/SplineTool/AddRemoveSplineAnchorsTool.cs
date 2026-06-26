#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.EditorTools;
using UnityEditor;
using System.Linq;
using UnityEditor.Overlays;

namespace Pinwheel.Poseidon
{
    [EditorTool("Add or remove anchors", typeof(SplineHandle))]
    public class AddRemoveSplineAnchorsTool : EditorTool
    {
        protected GUIContent m_toolbarIcon;
        public override GUIContent toolbarIcon
        {
            get
            {
                return m_toolbarIcon;
            }
        }

        Vector3? newAnchorCandidate = null;
        int? newAnchorInsertIndex = null;

        private void OnEnable()
        {
            m_toolbarIcon = new GUIContent(Resources.Load<Texture2D>("Poseidon/Textures/AddRemoveAnchorIcon"), "Add or remove anchors");
        }

        private void OnDisable()
        {
            if (m_toolbarIcon != null && m_toolbarIcon.image != null)
            {
                Resources.UnloadAsset(m_toolbarIcon.image);
            }
        }

        public override void OnActivated()
        {
        }

        public override void OnWillBeDeactivated()
        {
            foreach (var obj in targets)
            {
                if ((obj is SplineHandle water))
                {
                    //water.GenerateMesh();
                }
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            if (!(target is SplineHandle spline))
                return;
            spline.ValidateAnchors();
            SplineEditorUtilities.color = Color.cyan;
            SplineEditorUtilities.DrawSplinePath(spline, newAnchorCandidate, newAnchorInsertIndex);
            SplineEditorUtilities.DrawSplineAnchors(spline);
            HandleRemoveAnchor(spline);
            HandleAddAnchor(spline);
            DrawPivot(spline);
            CatchHotControl();
        }

        private void HandleRemoveAnchor(SplineHandle spline)
        {
            List<Transform> anchors = spline.anchors;

            for (int i = 0; i < anchors.Count; ++i)
            {
                Transform a = anchors[i];
                Vector3 positionOS = a.localPosition;
                Vector3 positionWS = a.position;
                float handleSize = 0.5f;
                Handles.color = Color.cyan;
                if (Handles.Button(positionWS, Quaternion.Euler(-90, 0, 0), handleSize, handleSize * 0.5f, Handles.CircleHandleCap))
                {
                    if (Event.current.control)
                    {
                        Transform anchor = spline.anchors[i];
                        DestroyImmediate(anchor.gameObject);
                        spline.anchors.RemoveAt(i);
                        EditorUtility.SetDirty(spline);
                    }
                    else if (Event.current.shift)
                    {
                    }
                    else
                    {
                    }
                    Event.current.Use();
                }
            }
        }

        private void HandleAddAnchor(SplineHandle spline)
        {
            int raycastLayer = PSplineToolConfig.Instance.RaycastLayer;
            Ray r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            RaycastHit hit;
            if (Physics.Raycast(r, out hit, 10000, LayerMask.GetMask(LayerMask.LayerToName(raycastLayer))))
            {
                bool isLeftMouseUp = Event.current.type == EventType.MouseUp && Event.current.button == 0;
                bool isShift = Event.current.shift;
                bool isCtrl = Event.current.control;
                if (isLeftMouseUp)
                {
                    if (!isShift)
                    {
                        return;
                    }

                    Vector3 offset = Vector3.up * PSplineToolConfig.Instance.YOffset;
                    Vector3 worldPos = hit.point + offset;
                    Vector3 localPos = spline.transform.InverseTransformPoint(worldPos);

                    GameObject anchorGO = new GameObject("~Anchor");
                    anchorGO.transform.parent = spline.transform;
                    anchorGO.transform.localPosition = localPos;
                    anchorGO.transform.localRotation = Quaternion.identity;
                    anchorGO.transform.localScale = Vector3.one;

                    if (isCtrl)
                    {
                        int insertIndex = AnchorUtilities.GetInsertIndex(spline.anchors.Select(a => a.position).ToList(), anchorGO.transform.position);
                        spline.anchors.Insert(insertIndex, anchorGO.transform);
                    }
                    else
                    {

                        spline.anchors.Add(anchorGO.transform);
                    }

                    EditorUtility.SetDirty(spline);
                    Event.current.Use();
                }
                else
                {
                    if (spline.anchors.Count > 1 && isShift)
                    {
                        if (isCtrl)
                        {
                            newAnchorInsertIndex = AnchorUtilities.GetInsertIndex(spline.anchors.Select(a => a.position).ToList(), hit.point);
                        }
                        else
                        {
                            newAnchorInsertIndex = null;
                        }
                        newAnchorCandidate = hit.point;
                    }
                    else
                    {
                        newAnchorCandidate = null;
                        newAnchorInsertIndex = null;
                    }
                }
            }
            else
            {
                newAnchorCandidate = null;
                newAnchorInsertIndex = null;
            }
        }

        private void DrawPivot(SplineHandle spline)
        {
            Vector3 pivot = spline.transform.position;
            float size = HandleUtility.GetHandleSize(pivot);

            Vector3 xStart = pivot + Vector3.left * size;
            Vector3 xEnd = pivot + Vector3.right * size;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = Handles.xAxisColor;
            Handles.DrawLine(xStart, xEnd);

            Vector3 yStart = pivot + Vector3.down * size;
            Vector3 yEnd = pivot + Vector3.up * size;
            Handles.color = Handles.yAxisColor;
            Handles.DrawLine(yStart, yEnd);

            Vector3 zStart = pivot + Vector3.back * size;
            Vector3 zEnd = pivot + Vector3.forward * size;
            Handles.color = Handles.zAxisColor;
            Handles.DrawLine(zStart, zEnd);
        }

        private void CatchHotControl()
        {
            int controlId = GUIUtility.GetControlID(this.GetHashCode(), FocusType.Passive);
            if (Event.current.type == EventType.MouseDown)
            {
                if (Event.current.button == 0)
                {
                    GUIUtility.hotControl = controlId;
                }
            }
            else if (Event.current.type == EventType.MouseUp)
            {
                if (GUIUtility.hotControl == controlId)
                {
                    //Return the hot control back to Unity, use the default
                    GUIUtility.hotControl = 0;
                }
            }
        }
    }

    [Overlay(typeof(SceneView), "Add or remove anchors")]
    public class AddRemoveAnchorOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(AddRemoveSplineAnchorsTool);

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Shift + Click to add anchor");
            EditorGUILayout.LabelField("Shift + Ctrl + Click to insert anchor");
            EditorGUILayout.LabelField("Ctrl + Click to remove anchor");
        }
    }
}

#endif