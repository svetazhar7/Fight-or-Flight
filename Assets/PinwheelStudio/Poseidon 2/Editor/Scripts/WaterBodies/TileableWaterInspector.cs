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
    [CustomEditor(typeof(TileableWater))]
    public class TileableWaterInspector : WaterBodyInspectorBase
    {
        private TileableWater m_instance;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_instance = target as TileableWater;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
        }

        class MeshGenGUI
        {
            public static readonly GUIContent PATTERN = new GUIContent("Pattern", "Polygon pattern of the plane grid");
            public static readonly GUIContent RESOLUTION = new GUIContent("Resolution", "Polygon density of the plane grid");
            public static readonly GUIContent TILE_SIZE = new GUIContent("Tile Size", "Size of a plane tile");
            public static readonly GUIContent NEED_NORMALS = new GUIContent("Need Normals", "Should it calculate and pack normal vectors in the mesh? Enable when the shader has tangent space normal maps.");
            public static readonly GUIContent NEED_TANGENTS = new GUIContent("Need Tangents", "Should it calculate and pack tangent vectors in the mesh? Enable when the shader has tangent space normal maps.");

            public static readonly GUIContent MESH = new GUIContent("Mesh", "The generated mesh used for rendering.");
        }

        protected override void OnMeshGenGUI()
        {
            EditorGUI.BeginChangeCheck();
            PlaneMeshPattern pattern = (PlaneMeshPattern)EditorGUILayout.EnumPopup(MeshGenGUI.PATTERN, m_instance.meshPattern);
            TileMeshDesc meshDesc = m_instance.tileMeshDesc;
            meshDesc.resolution = EditorGUILayout.DelayedIntField(MeshGenGUI.RESOLUTION, meshDesc.resolution);
            meshDesc.size = EditorGUILayout.DelayedFloatField(MeshGenGUI.TILE_SIZE, meshDesc.size);
            meshDesc.needNormals = EditorGUILayout.Toggle(MeshGenGUI.NEED_NORMALS, meshDesc.needNormals);
            meshDesc.needTangents = EditorGUILayout.Toggle(MeshGenGUI.NEED_TANGENTS, meshDesc.needTangents);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m_instance, $"Modify {m_instance.name}");
                m_instance.meshPattern = pattern;
                m_instance.tileMeshDesc = meshDesc;
                m_instance.GenerateMesh();
            }
            GUI.enabled = false;
            EditorGUILayout.ObjectField(MeshGenGUI.MESH, m_instance.sharedMesh, typeof(Mesh), false);
            GUI.enabled = true;
        }
    }

    [EditorTool("Edit Water Tiles", typeof(TileableWater))]
    public class EditWaterTileTool : EditorTool
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
            m_toolbarIcon = new GUIContent(Resources.Load<Texture2D>("Poseidon/Textures/EditWaterTilesIcon"), "Edit water tiles");
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
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            foreach (var obj in targets)
            {
                if (!(obj is TileableWater water))
                    continue;
                if (water == null)
                    continue;

                Plane plane = new Plane(Vector3.up, water.transform.position);
                Ray r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                float distance = -1;
                if (plane.Raycast(r, out distance))
                {
                    Vector3 hitPointWS = r.origin + r.direction * distance;
                    Vector3 hitPointOS = water.transform.InverseTransformPoint(hitPointWS);

                    Vector2Int tileIndex = water.PointOSToTileIndex(hitPointOS);
                    Vector3 tileOriginOS = water.TileIndexToTileOriginOS(tileIndex);
                    Vector3 tileCenterOS = tileOriginOS + new Vector3(water.tileMeshDesc.size * 0.5f, 0, water.tileMeshDesc.size * 0.5f);
                    Vector3 tileSizeOS = new Vector3(water.tileMeshDesc.size, 0, water.tileMeshDesc.size);

                    Vector3 tileCenterWS = water.transform.TransformPoint(tileCenterOS);
                    Vector3 tileSizeWS = water.transform.TransformVector(tileSizeOS);

                    Handles.color =
                        Event.current.shift ? Color.cyan :
                        Event.current.control ? Color.red :
                        Color.white;
                    Handles.DrawWireCube(tileCenterWS, tileSizeWS);

                    if (Event.current.type == EventType.MouseDrag || Event.current.type == EventType.MouseUp)
                    {
                        if (Event.current.button != 0)
                            return;
                        if (Event.current.control)
                        {
                            water.DestroyTile(tileIndex.x, tileIndex.y);
                            Utilities.MarkCurrentSceneDirty();
                            EditorUtility.SetDirty(water);
                            Event.current.Use();
                        }
                        else if (Event.current.shift)
                        {
                            if (!water.HasTile(tileIndex.x, tileIndex.y))
                            {
                                water.GetOrAddTile(tileIndex.x, tileIndex.y);
                                Utilities.MarkCurrentSceneDirty();
                                EditorUtility.SetDirty(water);
                                Event.current.Use();
                            }
                        }
                    }
                }

                CatchHotControl();
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

    [Overlay(typeof(SceneView), "Edit water tiles")]
    public class EditWaterTileOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(EditWaterTileTool);

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Shift + Click to add tile");
            EditorGUILayout.LabelField("Ctrl + Click to remove tile");
        }
    }
}

#endif