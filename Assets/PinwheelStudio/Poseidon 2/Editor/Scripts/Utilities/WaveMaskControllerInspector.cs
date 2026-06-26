#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine.UIElements;
using UnityEngine.Rendering;

namespace Pinwheel.Poseidon
{
    [CustomEditor(typeof(WaveMaskController))]
    public class WaveMaskControllerInspector : Editor
    {
        private WaveMaskController m_instance;

        private void OnEnable()
        {
            m_instance = target as WaveMaskController;
        }

        private class UI
        {
            public static readonly GUIContent ORIGIN = new GUIContent("Origin", "Position of the bottom left corner of the mask in world space");
            public static readonly GUIContent SIZE = new GUIContent("Size", "Size of the mask in world space");

            public static readonly GUIContent X = new GUIContent("X");
            public static readonly GUIContent Z = new GUIContent("Z");
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            Vector3 origin = m_instance.maskOriginWS;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UI.ORIGIN, GUILayout.Width(EditorGUIUtility.labelWidth));
            using (LabelWidthScope scope = new LabelWidthScope(12))
            {
                origin.x = EditorGUILayout.FloatField(UI.X, origin.x);
                origin.z = EditorGUILayout.FloatField(UI.Z, origin.z);
            }
            EditorGUILayout.EndHorizontal();

            Vector3 size = m_instance.maskSizeWS;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UI.SIZE, GUILayout.Width(EditorGUIUtility.labelWidth));
            using (LabelWidthScope scope = new LabelWidthScope(12))
            {
                size.x = EditorGUILayout.FloatField(UI.X, size.x);
                size.z = EditorGUILayout.FloatField(UI.Z, size.z);
            }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m_instance, $"Modify wave mask");
                EditorUtility.SetDirty(m_instance);
                m_instance.maskOriginWS = origin;
                m_instance.maskSizeWS = size;
            }
        }
    }

    [EditorTool("Edit Wave Mask", typeof(WaveMaskController))]
    public class EditWaveMaskTool : EditorTool
    {
        protected GUIContent m_toolbarIcon;
        public override GUIContent toolbarIcon
        {
            get
            {
                return m_toolbarIcon;
            }
        }

        public static EditWaveMaskTool active { get; private set; }

        private void OnEnable()
        {
            m_toolbarIcon = new GUIContent(Resources.Load<Texture2D>("Poseidon/Textures/EditWaveMaskIcon"), "Edit wave mask");
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
            base.OnActivated();
            active = this;
        }

        public override void OnWillBeDeactivated()
        {
            base.OnWillBeDeactivated();
            active = null;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            if (!(target is WaveMaskController controller))
                return;

            Vector3 originWS = controller.maskOriginWS;
            originWS.y = controller.transform.position.y;
            Vector3 sizeWS = controller.maskSizeWS;
            sizeWS.y = 0;
            Vector3 centerWS = originWS + sizeWS * 0.5f;

            using (Handles.DrawingScope scope = new Handles.DrawingScope(Color.white))
            {
                Handles.DrawWireCube(centerWS, sizeWS);
                centerWS = Handles.DoPositionHandle(centerWS, Quaternion.identity);

                float sizeDraggerHandleSize = 0.15f * HandleUtility.GetHandleSize(controller.transform.position);

                Vector3 leftEdgeCenter = centerWS + Vector3.left * sizeWS.x * 0.5f;
                EditorGUI.BeginChangeCheck();
                Vector3 leftDragOffset = Handles.Slider(leftEdgeCenter, Vector3.left, sizeDraggerHandleSize, Handles.CubeHandleCap, 1) - leftEdgeCenter;
                if (EditorGUI.EndChangeCheck())
                {
                    sizeWS.x += (-leftDragOffset.x);
                    centerWS.x += leftDragOffset.x * 0.5f;
                }

                Vector3 rightEdgeCenter = centerWS + Vector3.right * sizeWS.x * 0.5f;
                EditorGUI.BeginChangeCheck();
                Vector3 rightDragOffset = Handles.Slider(rightEdgeCenter, Vector3.right, sizeDraggerHandleSize, Handles.CubeHandleCap, 1) - rightEdgeCenter;
                if (EditorGUI.EndChangeCheck())
                {
                    sizeWS.x += rightDragOffset.x;
                    centerWS.x += rightDragOffset.x * 0.5f;
                }

                Vector3 bottomEdgeCenter = centerWS + Vector3.back * sizeWS.z * 0.5f;
                EditorGUI.BeginChangeCheck();
                Vector3 bottomDragOffset = Handles.Slider(bottomEdgeCenter, Vector3.back, sizeDraggerHandleSize, Handles.CubeHandleCap, 1) - bottomEdgeCenter;
                if (EditorGUI.EndChangeCheck())
                {
                    sizeWS.z += (-bottomDragOffset.z);
                    centerWS.z += bottomDragOffset.z * 0.5f;
                }

                Vector3 topEdgeCenter = centerWS + Vector3.forward * sizeWS.z * 0.5f;
                EditorGUI.BeginChangeCheck();
                Vector3 topDragOffset = Handles.Slider(topEdgeCenter, Vector3.back, sizeDraggerHandleSize, Handles.CubeHandleCap, 1) - topEdgeCenter;
                if (EditorGUI.EndChangeCheck())
                {
                    sizeWS.z += topDragOffset.z;
                    centerWS.z += topDragOffset.z * 0.5f;
                }
            }

            originWS = centerWS - sizeWS * 0.5f;
            controller.maskOriginWS = originWS;
            controller.maskSizeWS = sizeWS;
        }
    }

    [Overlay(typeof(SceneView), "Edit wave mask")]
    public class EditWaveMaskOverlay : Overlay, ITransientOverlay
    {
        const int CHANNEL_NONE = -1;
        const int CHANNEL_HEIGHT_MASK_R = 0;
        const int CHANNEL_CREST_MASK_G = 1;
        private static int[] channelOptionValues = new int[] { CHANNEL_NONE, CHANNEL_HEIGHT_MASK_R, CHANNEL_CREST_MASK_G };
        private static string[] channelOptionLabels = new string[] { "None", "Height Mask (R)", "Crest Mask (G)" };

        private static int selectedChannelForVisualization = CHANNEL_NONE;

        public override void OnCreated()
        {
            base.OnCreated();

            Camera.onPreCull += OnCameraPreCullBiRP;
#if POSEIDON_2_URP
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRenderingSRP;
#endif
        }

        public override void OnWillBeDestroyed()
        {
            Camera.onPreCull -= OnCameraPreCullBiRP;
#if POSEIDON_2_URP
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRenderingSRP;
#endif

            CleanUp();
            base.OnWillBeDestroyed();
        }

        public bool visible => ToolManager.activeToolType == typeof(EditWaveMaskTool);

        public override VisualElement CreatePanelContent()
        {
            IMGUIContainer imgui = new IMGUIContainer() { onGUIHandler = OnGUI };
            imgui.style.width = new StyleLength(new Length(300, LengthUnit.Pixel));
            return imgui;
        }

        public void OnGUI()
        {
            selectedChannelForVisualization = EditorGUILayout.IntPopup("Visualize", selectedChannelForVisualization, channelOptionLabels, channelOptionValues);
        }

        private void OnCameraPreCullBiRP(Camera camera)
        {
            RenderMaskVisualization(camera);
        }

#if POSEIDON_2_URP
        private void OnBeginCameraRenderingSRP(ScriptableRenderContext context, Camera camera)
        {
            RenderMaskVisualization(camera);
        }
#endif

        private void RenderMaskVisualization(Camera camera)
        {
            if (!visible)
                return;

            if (camera.cameraType != CameraType.SceneView)
                return;

            if (selectedChannelForVisualization == CHANNEL_NONE)
                return;

            if (EditWaveMaskTool.active == null)
                return;

            WaveMaskController controller = EditWaveMaskTool.active.target as WaveMaskController;
            if (controller == null)
                return;

            PlanarWaterBody water = controller.planarWaterBody;
            if (water.material == null)
                return;
            Init();

            Texture waveMaskTexture = Texture2D.blackTexture;
            if (water.material.HasProperty(PMat.WAVE_MASK))
            {
                waveMaskTexture = water.material.GetTexture(PMat.WAVE_MASK);
            }
            maskVisMaterial.SetTexture(PMat.WAVE_MASK, waveMaskTexture);

            if (selectedChannelForVisualization == CHANNEL_HEIGHT_MASK_R)
            {
                maskVisMaterial.EnableKeyword("HEIGHT");
                maskVisMaterial.DisableKeyword("CREST");
            }
            else if (selectedChannelForVisualization == CHANNEL_CREST_MASK_G)
            {
                maskVisMaterial.DisableKeyword("HEIGHT");
                maskVisMaterial.EnableKeyword("CREST");
            }

            Graphics.DrawMesh(
                quadMesh,
                Matrix4x4.TRS(controller.maskOriginWS, Quaternion.identity, controller.maskSizeWS),
                maskVisMaterial,
                0,
                camera,
                0,
                null,
                ShadowCastingMode.Off,
                false,
                null,
                LightProbeUsage.Off,
                null);
        }

        private Mesh quadMesh;
        private Material maskVisMaterial;
        private void Init()
        {
            if (quadMesh == null)
            {
                quadMesh = new Mesh();
                quadMesh.vertices = new Vector3[] { Vector3.zero, Vector3.forward, Vector3.forward + Vector3.right, Vector3.right };
                quadMesh.uv = new Vector2[] { Vector2.zero, Vector2.up, Vector2.up + Vector2.right, Vector2.right };
                quadMesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
                quadMesh.RecalculateNormals();
                quadMesh.RecalculateTangents();
                quadMesh.RecalculateBounds();
            }

            if (maskVisMaterial == null)
            {
                maskVisMaterial = new Material(Shader.Find("Hidden/Poseidon/WaveMaskVisualizer"));
            }
        }

        private void CleanUp()
        {
            if (quadMesh != null)
            {
                Object.DestroyImmediate(quadMesh);
                quadMesh = null;
            }

            if (maskVisMaterial != null)
            {
                Object.DestroyImmediate(maskVisMaterial);
                maskVisMaterial = null;
            }
        }
    }
}

#endif