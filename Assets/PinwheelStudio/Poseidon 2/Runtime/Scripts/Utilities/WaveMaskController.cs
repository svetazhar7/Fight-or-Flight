#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace Pinwheel.Poseidon
{
    [RequireComponent(typeof(PlanarWaterBody))]
    [ExecuteInEditMode]
    public class WaveMaskController : MonoBehaviour
    {
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
        protected Vector3 m_maskOriginWS;
        public Vector3 maskOriginWS
        {
            get
            {
                return m_maskOriginWS;
            }
            set
            {
                m_maskOriginWS = value;
            }
        }

        [SerializeField]
        protected Vector3 m_maskSizeWS;
        public Vector3 maskSizeWS
        {
            get
            {
                return m_maskSizeWS;
            }
            set
            {
                m_maskSizeWS = value;
            }
        }

        private void OnEnable()
        {
            Camera.onPreCull += OnCameraPreCullBiRP;
#if POSEIDON_2_URP
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRenderingSRP;
#endif
        }

        private void OnDisable()
        {
            Camera.onPreCull -= OnCameraPreCullBiRP;
#if POSEIDON_2_URP
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRenderingSRP;
#endif
        }

        private void OnCameraPreCullBiRP(Camera camera)
        {
            UpdateWaveMaskProperties();
        }

#if POSEIDON_2_URP
        private void OnBeginCameraRenderingSRP(ScriptableRenderContext context, Camera camera)
        {
            UpdateWaveMaskProperties();
        }
#endif

        private void UpdateWaveMaskProperties()
        {
            if (planarWaterBody.material == null)
                return;
            if (planarWaterBody.material.HasProperty(PMat.WAVE_MASK_BOUNDS))
            {
                planarWaterBody.material.SetVector(PMat.WAVE_MASK_BOUNDS, new Vector4(maskOriginWS.x, maskOriginWS.z, maskSizeWS.x, maskSizeWS.z));
            }
        }
    }
}

#endif