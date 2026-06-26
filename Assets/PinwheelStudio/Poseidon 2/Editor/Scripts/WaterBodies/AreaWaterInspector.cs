#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using System.Linq;
using UnityEditor.Overlays;

namespace Pinwheel.Poseidon
{
    [CustomEditor(typeof(AreaWater))]
    public class AreaWaterInspector : WaterBodyInspectorBase
    {
        private AreaWater m_instance;

        protected override bool shouldDrawMaterialEditor => false;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_instance = target as AreaWater;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
        }

        class MeshGenGUI
        {
            public static string ID = "area-water-meshgen";
            public static string HEADER = "Mesh Generation";

            public static readonly GUIContent PATTERN = new GUIContent("Pattern", "Polygon pattern of the plane grid");
            public static readonly GUIContent RESOLUTION = new GUIContent("Resolution", "Polygon density of the plane grid");
            public static readonly GUIContent NEED_NORMALS = new GUIContent("Need Normals", "Should it calculate and pack normal vectors in the mesh? Enable when the shader has tangent space normal maps.");
            public static readonly GUIContent NEED_TANGENTS = new GUIContent("Need Tangents", "Should it calculate and pack tangent vectors in the mesh? Enable when the shader has tangent space normal maps.");

            public static readonly GUIContent MESH = new GUIContent("Mesh", "The generated mesh used for rendering.");
        }

        protected override void OnMeshGenGUI()
        {
            EditorGUI.BeginChangeCheck();
            PlaneMeshPattern pattern = (PlaneMeshPattern)EditorGUILayout.EnumPopup(MeshGenGUI.PATTERN, m_instance.meshPattern);
            AreaMeshDesc meshDesc = m_instance.meshDesc;
            meshDesc.resolution = EditorGUILayout.DelayedIntField(MeshGenGUI.RESOLUTION, meshDesc.resolution);
            meshDesc.needNormals = EditorGUILayout.Toggle(MeshGenGUI.NEED_NORMALS, meshDesc.needNormals);
            meshDesc.needTangents = EditorGUILayout.Toggle(MeshGenGUI.NEED_TANGENTS, meshDesc.needTangents);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m_instance, $"Modify {m_instance.name}");
                m_instance.meshPattern = pattern;
                m_instance.meshDesc = meshDesc;
                m_instance.GenerateMesh();
            }
            GUI.enabled = false;
            EditorGUILayout.ObjectField(MeshGenGUI.MESH, m_instance.sharedMesh, typeof(Mesh), false);
            GUI.enabled = true;
        }
    }

    [EditorTool("Edit Water Area", typeof(AreaWater))]
    public class EditWaterAreaTool : EditorTool
    {
        protected GUIContent m_toolbarIcon;
        public override GUIContent toolbarIcon
        {
            get
            {
                return m_toolbarIcon;
            }
        }

        private int m_selectedAnchorIndex;

        private void OnEnable()
        {
            m_toolbarIcon = new GUIContent(Resources.Load<Texture2D>("Poseidon/Textures/EditWaterAreaIcon"), "Edit water area");
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
            m_selectedAnchorIndex = -1;
        }

        public override void OnWillBeDeactivated()
        {
            foreach (var obj in targets)
            {
                if ((obj is AreaWater water) && water != null)
                {
                    water.GenerateMesh();
                }
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            foreach (var obj in targets)
            {
                if (!(obj is AreaWater water))
                    continue;

                if (water == null)
                    continue;

                HandleSelectTranslateRemoveAnchors(water);
                HandleAddAnchor(water);
                CatchHotControl();
            }
        }

        private void HandleSelectTranslateRemoveAnchors(AreaWater water)
        {
            List<Vector3> localPositions = water.anchors;
            if (localPositions.Count == 0)
                return;
            if (localPositions.Count >= 2)
            {
                List<Vector3> worldPositions = new List<Vector3>();
                for (int i = 0; i < localPositions.Count; ++i)
                {
                    worldPositions.Add(water.transform.TransformPoint(localPositions[i]));
                }

                Handles.color = new Color(0, 1, 1, 1);
                Handles.DrawPolyLine(worldPositions.ToArray());
                Handles.color = new Color(0, 1, 1, 0.25f);
                Handles.DrawLine(worldPositions[0], worldPositions[worldPositions.Count - 1]);
            }

            for (int i = 0; i < localPositions.Count; ++i)
            {
                Vector3 localPos = localPositions[i];
                Vector3 worldPos = water.transform.TransformPoint(localPos);
                float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.2f;
                if (i == m_selectedAnchorIndex)
                {
                    Handles.color = Handles.selectedColor;
                    Handles.SphereHandleCap(0, worldPos, Quaternion.identity, handleSize, EventType.Repaint);
                    worldPos = Handles.PositionHandle(worldPos, Quaternion.identity);
                    localPos = water.transform.InverseTransformPoint(worldPos);
                    localPos.y = 0;
                    localPositions[i] = localPos;
                }
                else
                {
                    Handles.color = Color.cyan;
                    if (Handles.Button(worldPos, Quaternion.identity, handleSize, handleSize * 0.5f, Handles.SphereHandleCap))
                    {
                        if (Event.current.control)
                        {
                            m_selectedAnchorIndex = -1;
                            localPositions.RemoveAt(i);
                        }
                        else
                        {
                            m_selectedAnchorIndex = i;
                        }
                    }
                }
            }

            if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                m_selectedAnchorIndex = -1;
            }
        }

        private void HandleAddAnchor(AreaWater water)
        {
            if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Plane plane = new Plane(Vector3.up, water.transform.position);
                Ray r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                float distance = -1;
                if (plane.Raycast(r, out distance))
                {
                    Vector3 hitWorldPos = r.origin + r.direction * distance;
                    Vector3 hitLocalPos = water.transform.InverseTransformPoint(hitWorldPos);
                    if (Event.current.shift)
                    {
                        if (Event.current.control)
                        {
                            AnchorUtilities.Insert(water.anchors, hitLocalPos);
                            Event.current.Use();
                        }
                        else
                        {
                            water.anchors.Add(hitLocalPos);
                            Event.current.Use();
                        }
                    }
                }
            }
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

    [Overlay(typeof(SceneView), "Edit water area")]
    public class EditWaterAreaOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(EditWaterAreaTool);

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Click to select");
            EditorGUILayout.LabelField("Shift + Click to add anchor");
            EditorGUILayout.LabelField("Ctrl + Shift + Click to insert anchor");
            EditorGUILayout.LabelField("Ctrl + Click to remove anchor");
        }
    }
}

#endif