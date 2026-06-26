#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if POSEIDON_2_URP
using UnityEngine.Rendering.Universal;
#endif
using UnityEngine.Rendering;

namespace Pinwheel.Poseidon
{
    [RequireComponent(typeof(PlanarWaterBody))]
    [ExecuteInEditMode]
    public class PlanarReflectionRenderer : MonoBehaviour
    {
        protected Camera m_mainCamera;
        public Camera mainCamera
        {
            get
            {
                if (m_mainCamera == null)
                {
                    m_mainCamera = Camera.main;
                }
                return m_mainCamera;
            }
        }

        [SerializeField]
        private PlanarWaterBody m_planarWaterBody;
        public PlanarWaterBody planarWaterBody
        {
            get
            {
                if (m_planarWaterBody == null)
                {
                    m_planarWaterBody = GetComponent<PlanarWaterBody>();
                }
                return m_planarWaterBody;
            }
        }

        [SerializeField]
        private int m_textureResolution;
        public int textureResolution
        {
            get
            {
                return m_textureResolution;
            }
            set
            {
                int oldValue = m_textureResolution;
                int newValue = Mathf.Clamp(value, 32, 2048);
                m_textureResolution = newValue;
                if (oldValue != newValue)
                {
                    if (m_reflectionTexture != null)
                    {
                        if (m_reflectionCamera != null && m_reflectionCamera.targetTexture == m_reflectionTexture)
                        {
                            m_reflectionCamera.targetTexture = null;
                        }

                        m_reflectionTexture.Release();
                        Object.DestroyImmediate(m_reflectionTexture);
                        m_reflectionTexture = null;
                    }
                }
            }
        }

        [SerializeField]
        private float m_clipPlaneOffset;
        public float clipPlaneOffset
        {
            get
            {
                return m_clipPlaneOffset;
            }
            set
            {
                m_clipPlaneOffset = value;
            }
        }

        [SerializeField]
        private LayerMask m_reflectionLayers;
        public LayerMask reflectionLayers
        {
            get
            {
                return m_reflectionLayers;
            }
            set
            {
                m_reflectionLayers = value;
            }
        }

        [SerializeField]
        private int m_rendererIndex;
        public int rendererIndex
        {
            get
            {
                return m_rendererIndex;
            }
            set
            {
                m_rendererIndex = value;
            }
        }

        private RenderTexture m_reflectionTexture;
        private Camera m_reflectionCamera;

        private void Reset()
        {
            m_textureResolution = 128;
            m_reflectionLayers = LayerMask.GetMask("Default");
            m_rendererIndex = 0;
        }

        private void OnEnable()
        {
            Camera.onPreCull += OnCameraPreCullBiRP;
#if POSEIDON_2_URP
            RenderPipelineManager.beginContextRendering += OnBeginContextRenderingSRP;
#endif
        }

        private void OnDisable()
        {
            if (m_reflectionCamera != null)
            {
                m_reflectionCamera.targetTexture = null;
                Object.DestroyImmediate(m_reflectionCamera.gameObject);
            }
            if (m_reflectionTexture != null)
            {
                m_reflectionTexture.Release();
                Object.DestroyImmediate(m_reflectionTexture);
                m_reflectionTexture = null;
            }


            Camera.onPreCull -= OnCameraPreCullBiRP;
#if POSEIDON_2_URP
            RenderPipelineManager.beginContextRendering -= OnBeginContextRenderingSRP;
#endif
        }

        private void OnCameraPreCullBiRP(Camera cam)
        {
            if (Application.isPlaying)
            {
                bool isBackface = mainCamera.transform.position.y < transform.position.y;
                if (isBackface)
                    return;

                if (mainCamera.stereoEnabled)
                    return;

                if (m_reflectionTexture == null)
                {
                    m_reflectionTexture = new RenderTexture(m_textureResolution, m_textureResolution, 16, RenderTextureFormat.ARGB32);
                    m_reflectionTexture.name = "~ReflectionTexture";
                    m_reflectionTexture.Create();
                }

                if (m_reflectionCamera == null)
                {
                    GameObject g = new GameObject();
                    g.name = "~ReflectionCamera";
                    g.hideFlags = HideFlags.DontSave;

                    Camera rCam = g.AddComponent<Camera>();
                    rCam.enabled = false;

                    g.transform.parent = this.transform;
                    g.transform.localPosition = Vector3.zero;
                    g.transform.localRotation = Quaternion.identity;
                    g.transform.localScale = Vector3.one;
                    m_reflectionCamera = rCam;
                }

                //define reflection plane by position & normal in world space
                Vector3 planePos = transform.position;
                Vector3 planeNormal = transform.up;

                bool lastInvertCulling = GL.invertCulling;
                GL.invertCulling = !lastInvertCulling;

                //disable pixel lights
                int lastPixelLightCount = QualitySettings.pixelLightCount;
                QualitySettings.pixelLightCount = 0;

                //set up camera and render
                m_reflectionCamera.enabled = false;
                m_reflectionCamera.targetTexture = m_reflectionTexture;
                MatchCameraSettings(mainCamera, m_reflectionCamera);

                // Reflect camera around reflection plane
                float d = -Vector3.Dot(planeNormal, planePos) - clipPlaneOffset;
                Vector4 reflectionPlane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, d);

                Matrix4x4 reflection = CalculateReflectionMatrix(reflectionPlane, Camera.MonoOrStereoscopicEye.Mono);
                Vector3 oldpos = mainCamera.transform.position;
                Vector3 newpos = reflection.MultiplyPoint(oldpos);
                m_reflectionCamera.worldToCameraMatrix = mainCamera.worldToCameraMatrix * reflection;

                // Setup oblique projection matrix so that near plane is our reflection
                // plane. This way we clip everything below/above it for free.
                Vector4 clipPlane = CameraSpacePlane(m_reflectionCamera, planePos, planeNormal, clipPlaneOffset, 1.0f);
                Matrix4x4 obliqueMatrix = mainCamera.CalculateObliqueMatrix(clipPlane);
                m_reflectionCamera.projectionMatrix = obliqueMatrix;

                // Set custom culling matrix from the current camera
                m_reflectionCamera.cullingMatrix = mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix;
                m_reflectionCamera.cullingMask = ~(1 << 4) & reflectionLayers.value; // never render water layer
                m_reflectionCamera.transform.position = newpos;
                Vector3 euler = mainCamera.transform.eulerAngles;
                m_reflectionCamera.transform.eulerAngles = new Vector3(-euler.x, euler.y, euler.z);

                if (m_reflectionCamera.transform.rotation != Quaternion.identity)
                {
                    m_reflectionCamera.Render();
                }

                QualitySettings.pixelLightCount = lastPixelLightCount;
                GL.invertCulling = lastInvertCulling;

                if (planarWaterBody != null)
                {
                    planarWaterBody.SetPlanarReflectionTexture(m_reflectionTexture);
                }
            }
            else
            {
                if (planarWaterBody != null)
                {
                    planarWaterBody.SetPlanarReflectionTexture(Texture2D.whiteTexture);
                }
            }
        }

#if POSEIDON_2_URP
        private void OnBeginContextRenderingSRP(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (Application.isPlaying)
            {
                bool isBackface = mainCamera.transform.position.y < transform.position.y;
                if (isBackface)
                    return;

                if (mainCamera.stereoEnabled)
                    return;

                if (m_reflectionTexture == null)
                {
                    m_reflectionTexture = new RenderTexture(m_textureResolution, m_textureResolution, 16, RenderTextureFormat.ARGB32);
                    m_reflectionTexture.name = "~ReflectionTexture";
                    m_reflectionTexture.Create();
                }

                if (m_reflectionCamera == null)
                {
                    GameObject g = new GameObject();
                    g.name = "~ReflectionCamera";
                    g.hideFlags = HideFlags.DontSave;

                    Camera rCam = g.AddComponent<Camera>();
                    rCam.enabled = true;

                    UniversalAdditionalCameraData addCamData = g.AddComponent<UniversalAdditionalCameraData>();
                    addCamData.renderShadows = false;
                    addCamData.renderType = CameraRenderType.Base;
                    addCamData.SetRenderer(m_rendererIndex);

                    g.transform.parent = this.transform;
                    g.transform.localPosition = Vector3.zero;
                    g.transform.localRotation = Quaternion.identity;
                    g.transform.localScale = Vector3.one;
                    m_reflectionCamera = rCam;
                }

                if (cameras.Contains(m_reflectionCamera))
                {
                    cameras.Remove(m_reflectionCamera);
                    cameras.Insert(0, m_reflectionCamera);
                }

                //define reflection plane by position & normal in world space
                Vector3 planePos = transform.position;
                Vector3 planeNormal = transform.up;

                bool lastInvertCulling = GL.invertCulling;
                GL.invertCulling = !lastInvertCulling;

                //set up camera and render
                m_reflectionCamera.targetTexture = m_reflectionTexture;
                MatchCameraSettings(mainCamera, m_reflectionCamera);

                // Reflect camera around reflection plane
                float d = -Vector3.Dot(planeNormal, planePos) - clipPlaneOffset;
                Vector4 reflectionPlane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, d);

                Matrix4x4 reflection = CalculateReflectionMatrix(reflectionPlane, Camera.MonoOrStereoscopicEye.Mono);
                Vector3 oldpos = mainCamera.transform.position;
                Vector3 newpos = reflection.MultiplyPoint(oldpos);
                m_reflectionCamera.worldToCameraMatrix = mainCamera.worldToCameraMatrix * reflection;

                // Setup oblique projection matrix so that near plane is our reflection
                // plane. This way we clip everything below/above it for free.
                Vector4 clipPlane = CameraSpacePlane(m_reflectionCamera, planePos, planeNormal, clipPlaneOffset, 1.0f);
                Matrix4x4 obliqueMatrix = mainCamera.CalculateObliqueMatrix(clipPlane);
                m_reflectionCamera.projectionMatrix = obliqueMatrix;

                // Set custom culling matrix from the current camera
                m_reflectionCamera.cullingMatrix = mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix;
                m_reflectionCamera.cullingMask = ~(1 << 4) & reflectionLayers.value; // never render water layer
                m_reflectionCamera.transform.position = newpos;
                Vector3 euler = mainCamera.transform.eulerAngles;
                m_reflectionCamera.transform.eulerAngles = new Vector3(-euler.x, euler.y, euler.z);

                //QualitySettings.pixelLightCount = lastPixelLightCount;
                GL.invertCulling = lastInvertCulling;

                if (planarWaterBody != null)
                {
                    planarWaterBody.SetPlanarReflectionTexture(m_reflectionTexture);
                }
            }
            else
            {
                if (planarWaterBody != null)
                {
                    planarWaterBody.SetPlanarReflectionTexture(Texture2D.whiteTexture);
                }
            }
        }
#endif

        private void MatchCameraSettings(Camera src, Camera dest)
        {
            if (dest == null)
            {
                return;
            }
            // set water camera to clear the same way as current camera
            dest.clearFlags = src.clearFlags;
            dest.backgroundColor = src.backgroundColor;

            // update other values to match current camera.
            // even if we are supplying custom camera&projection matrices,
            // some of values are used elsewhere (e.g. skybox uses far plane)
            //dest.ResetWorldToCameraMatrix();
            dest.farClipPlane = src.farClipPlane;
            dest.nearClipPlane = src.nearClipPlane;
            dest.orthographic = src.orthographic;
            dest.fieldOfView = src.fieldOfView;
            dest.aspect = src.aspect;
            dest.orthographicSize = src.orthographicSize;
            dest.depthTextureMode = DepthTextureMode.None;
            dest.depth = -100;
#if !POSEIDON_URP
            dest.stereoTargetEye = StereoTargetEyeMask.None;
#endif
        }

        private static Matrix4x4 CalculateReflectionMatrix(Vector4 plane, Camera.MonoOrStereoscopicEye eye = Camera.MonoOrStereoscopicEye.Mono)
        {
            Matrix4x4 reflectionMat = new Matrix4x4();

            reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
            reflectionMat.m01 = (-2F * plane[0] * plane[1]);
            reflectionMat.m02 = (-2F * plane[0] * plane[2]);
            reflectionMat.m03 = (-2F * plane[3] * plane[0]);

            reflectionMat.m10 = (-2F * plane[1] * plane[0]);
            reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
            reflectionMat.m12 = (-2F * plane[1] * plane[2]);
            reflectionMat.m13 = (-2F * plane[3] * plane[1]);

            reflectionMat.m20 = (-2F * plane[2] * plane[0]);
            reflectionMat.m21 = (-2F * plane[2] * plane[1]);
            reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
            reflectionMat.m23 = (-2F * plane[3] * plane[2]);

            reflectionMat.m30 = 0F;
            reflectionMat.m31 = 0F;
            reflectionMat.m32 = 0F;
            reflectionMat.m33 = 1F;

            //reflectionMat = Matrix4x4.Translate(new Vector3(float0, 0, 0))*reflectionMat ;

            return reflectionMat;
        }

        private static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float clipPlaneOffset, float sideSign)
        {
            Vector3 offsetPos = pos + normal * clipPlaneOffset;
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(offsetPos);
            Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }
    }
}
#endif