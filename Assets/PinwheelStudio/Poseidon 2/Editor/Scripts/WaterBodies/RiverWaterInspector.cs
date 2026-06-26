#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using System.Linq;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace Pinwheel.Poseidon
{
    [CustomEditor(typeof(RiverWater))]
    public class RiverWaterInspector : WaterBodyInspectorBase
    {
        private RiverWater m_instance;
        private SerializedProperty m_splineListProperty;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_instance = target as RiverWater;
            m_splineListProperty = new SerializedObject(target).FindProperty("m_splines");
            SceneView.duringSceneGui += DuringSceneGUI;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            m_splineListProperty.Dispose();
            SceneView.duringSceneGui -= DuringSceneGUI;
        }

        class MeshGenGUI
        {
            public static string ID = "river-water-meshgen";
            public static string HEADER = "Mesh Generation";

            public static readonly GUIContent PATTERN = new GUIContent("Pattern", "Polygon pattern of the plane grid");
            public static readonly GUIContent WIDTH = new GUIContent("Width", "");
            public static readonly GUIContent VERTEX_DISTANCE = new GUIContent("Vertex Distance", "");
            public static readonly GUIContent SEGMENT_LENGTH = new GUIContent("Segment Length", "");
            public static readonly GUIContent NEED_NORMALS = new GUIContent("Need Normals", "Should it calculate and pack normal vectors in the mesh? Enable when the shader has tangent space normal maps.");
            public static readonly GUIContent NEED_TANGENTS = new GUIContent("Need Tangents", "Should it calculate and pack tangent vectors in the mesh? Enable when the shader has tangent space normal maps.");

            public static readonly GUIContent SPLINES = new GUIContent("Splines", "List of splines for generating river meshes");
        }

        protected override void OnMeshGenGUI()
        {
            EditorGUI.BeginChangeCheck();
            RiverMeshDesc meshDesc = m_instance.meshDesc;
            meshDesc.width = EditorGUILayout.DelayedFloatField(MeshGenGUI.WIDTH, meshDesc.width);
            meshDesc.vertexDistance = EditorGUILayout.DelayedFloatField(MeshGenGUI.VERTEX_DISTANCE, meshDesc.vertexDistance);
            meshDesc.segmentLength = EditorGUILayout.DelayedFloatField(MeshGenGUI.SEGMENT_LENGTH, meshDesc.segmentLength);
            meshDesc.needNormals = EditorGUILayout.Toggle(MeshGenGUI.NEED_NORMALS, meshDesc.needNormals);
            meshDesc.needTangents = EditorGUILayout.Toggle(MeshGenGUI.NEED_TANGENTS, meshDesc.needTangents);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m_instance, $"Modify {m_instance.name}");
                m_instance.meshDesc = meshDesc;
                m_instance.GenerateMesh();
            }

            EditorGUILayout.PropertyField(m_splineListProperty, MeshGenGUI.SPLINES, true);
            m_splineListProperty.serializedObject.ApplyModifiedProperties();
            m_splineListProperty.serializedObject.Update();
        }

        public static bool drawSplineSamples;
        public static bool drawSplineNormals;
        public static bool drawSplineTangents;

        //[Overlay(typeof(SceneView), "River Debug")]
        //public class RiverDebugOverlay : IMGUIOverlay, ITransientOverlay
        //{
        //    public bool visible
        //    {
        //        get
        //        {
        //            return Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<RiverWater>() != null;
        //        }
        //    }

        //    public override void OnGUI()
        //    {
        //        drawSplineSamples = EditorGUILayout.ToggleLeft("Draw samples", drawSplineSamples);
        //        drawSplineNormals = EditorGUILayout.ToggleLeft("Draw normals", drawSplineNormals);
        //        drawSplineTangents = EditorGUILayout.ToggleLeft("Draw tangents", drawSplineTangents);
        //    }
        //}

        private void DuringSceneGUI(SceneView sv)
        {
            foreach (SplineHandle spline in m_instance.splines)
            {
                if (spline != null && spline.isActiveAndEnabled)
                {
                    SplineEditorUtilities.color = Color.white;
                    SplineEditorUtilities.DrawSplinePath(spline);
                    SplineEditorUtilities.DrawSplineAnchors(spline);
                }
            }

            //DrawJunctionsRank();

            //if (drawSplineSamples)
            //{
            //    DrawSplineSamples();
            //}

            //if (drawSplineNormals)
            //{
            //    DrawSplineNormals();
            //}

            //if (drawSplineTangents)
            //{
            //    DrawSplineTangents();
            //}

            //DrawDelaunayDebug();
        }

        private static GUIStyle m_debugLabelStyle;
        private static GUIStyle debugLabelStyle
        {
            get
            {
                if (m_debugLabelStyle == null)
                {
                    m_debugLabelStyle = new GUIStyle(EditorStyles.label);
                    m_debugLabelStyle.alignment = TextAnchor.UpperLeft;
                    m_debugLabelStyle.wordWrap = true;
                    m_debugLabelStyle.normal.textColor = Color.cyan;
                    m_debugLabelStyle.fontSize = 16;
                    m_debugLabelStyle.richText = true;
                }

                return m_debugLabelStyle;
            }
        }

        private void DrawJunctionsRank()
        {
            if (m_instance.anchorsRank == null)
                return;

            foreach (Transform junction in m_instance.anchorsRank.Keys)
            {
                int rank = m_instance.anchorsRank[junction];
                Handles.Label(junction.position, $"[{rank}]", debugLabelStyle);
            }
        }

        private void DrawSplineSamples()
        {
            if (m_instance.positionSamplesWS == null)
                return;
            for (int i = 0; i < m_instance.positionSamplesWS.Count; ++i)
            {
                Vector4 p = m_instance.positionSamplesWS[i];
                Handles.matrix = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
                if (Mathf.Approximately(p.w, RiverWater.W_SAMPLE))
                {
                    Handles.color = Color.yellow;
                    Handles.DrawSolidDisc(Vector3.zero, Vector3.up, 0.25f);
                }
                else if (Mathf.Approximately(p.w, RiverWater.W_BREAK_POINT))
                {
                    Handles.color = Color.red;
                    Handles.DrawSolidDisc(Vector3.zero, Vector3.up, 0.25f);
                }
                else if (Mathf.Approximately(p.w, RiverWater.W_START_SPLINE) ||
                    Mathf.Approximately(p.w, RiverWater.W_END_SPLINE))
                {
                    Handles.color = Color.magenta;
                    Handles.DrawSolidDisc(Vector3.zero, Vector3.up, 0.25f);
                }
                else if (Mathf.Approximately(p.w, RiverWater.W_JUNCTION))
                {
                    Handles.color = Color.green;
                    Handles.DrawSolidDisc(Vector3.zero, Vector3.up, 0.25f);
                }
            }

            Handles.matrix = Matrix4x4.identity;
        }

        private void DrawDelaunayDebug()
        {
            if (m_instance.verticesForDelaunayDebug == null)
                return;
            int i = 0;
            foreach (var vertexList in m_instance.debugDelaunayVerticesByAnchor.Values)
            {
                Handles.color = Utilities.GetDebugColor(i);
                foreach (Vector3 v in vertexList)
                {
                    Vector3 pWS = v;
                    Handles.DrawSolidDisc(pWS, Vector3.up, 0.1f);
                }
                i++;
            }
        }

        private void DrawSplineNormals()
        {
            if (m_instance.positionSamplesWS == null ||
                m_instance.normalsWS == null)
                return;

            Handles.color = Color.cyan;
            for (int i = 0; i < m_instance.positionSamplesWS.Count; ++i)
            {
                Vector3 posWS = m_instance.positionSamplesWS[i];
                Vector3 normalWS = m_instance.normalsWS[i];
                Handles.DrawLine(posWS, posWS + normalWS);
            }
        }
    }

    [EditorTool("Add spline to river", typeof(RiverWater))]
    public class AddSplineToRiverTool : EditorTool
    {
        protected GUIContent m_toolbarIcon;
        public override GUIContent toolbarIcon
        {
            get
            {
                return m_toolbarIcon;
            }
        }

        private void OnEnable()
        {
            m_toolbarIcon = new GUIContent(Resources.Load<Texture2D>("Poseidon/Textures/EditRiverIcon"), "Add spline to river");
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
            if (target is RiverWater river)
            {
                SplineHandle spline = river.AddSpline();
                Selection.activeGameObject = spline.gameObject;
            }
        }

        public override void OnWillBeDeactivated()
        {

        }
    }
}

#endif