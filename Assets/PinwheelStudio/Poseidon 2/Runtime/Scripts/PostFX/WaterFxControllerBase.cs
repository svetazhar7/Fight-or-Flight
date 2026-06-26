#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Events;

#if POSEIDON_2_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Pinwheel.Poseidon.FX
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(PlanarWaterBody))]
    public abstract class WaterFxControllerBase : MonoBehaviour
    {
        [SerializeField]
        protected PlanarWaterBody m_waterBody;
        public PlanarWaterBody waterBody
        {
            get
            {
                if (m_waterBody == null)
                {
                    m_waterBody = GetComponent<PlanarWaterBody>();
                }
                return m_waterBody;
            }
        }

        [SerializeField]
        protected Vector3 m_volumeExtent;
        public Vector3 volumeExtent
        {
            get
            {
                return m_volumeExtent;
            }
            set
            {
                Vector3 v = value;
                //v.x = Mathf.Max(0, v.x);
                //v.y = Mathf.Max(1, v.y);
                //v.z = Mathf.Max(0, v.z);
                m_volumeExtent = v;
            }
        }

        [SerializeField]
        protected int m_volumeLayer;
        public int volumeLayer
        {
            get
            {
                return m_volumeLayer;
            }
            set
            {
                m_volumeLayer = value;
            }
        }

        [SerializeField]
        protected float m_wetLensDuration;
        public float wetLensDuration
        {
            get
            {
                return m_wetLensDuration;
            }
            set
            {
                m_wetLensDuration = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected AnimationCurve m_wetLensFadeOutCurve;
        public AnimationCurve wetLensFadeOutCurve
        {
            get
            {
                return m_wetLensFadeOutCurve;
            }
            set
            {
                m_wetLensFadeOutCurve = value;
            }
        }

        [SerializeField]
        private UnityEvent m_onEnterWater;
        public UnityEvent onEnterWater
        {
            get
            {
                return m_onEnterWater;
            }
            set
            {
                m_onEnterWater = value;
            }
        }

        [SerializeField]
        private UnityEvent m_onExitWater;
        public UnityEvent onExitWater
        {
            get
            {
                return m_onExitWater;
            }
            set
            {
                m_onExitWater = value;
            }
        }

        protected float lastDistanceToSurface { get; private set; }
        protected float wetLensTime { get; private set; }

        protected virtual void Reset()
        {
            m_waterBody = GetComponent<PlanarWaterBody>();
            m_volumeExtent = new Vector3(0, 100, 0);
            m_volumeLayer = LayerMask.NameToLayer("Default");
            m_wetLensDuration = 3;
            m_wetLensFadeOutCurve = AnimationCurve.Linear(0, 1, 1, 0);
        }

        protected virtual void OnEnable()
        {
            Camera.onPreCull += OnPreCullBiRP;
#if POSEIDON_2_URP
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRenderingURP;
#endif
            PoseidonWaterBody.shapeChanged += OnWaterShapeChanged;

            if (Camera.main != null)
            {
                float waterHeightWS = GetWaterHeightWS(Camera.main.transform.position);
                lastDistanceToSurface = Camera.main.transform.position.y - waterHeightWS;
            }

            wetLensTime = Mathf.Infinity;

            if (waterBody != null)
            {
                SetupVolumes();
            }
        }

        protected virtual void OnDisable()
        {
            Camera.onPreCull -= OnPreCullBiRP;
#if POSEIDON_2_URP
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRenderingURP;
#endif
            PoseidonWaterBody.shapeChanged -= OnWaterShapeChanged;

            ClearVolumes();
        }

        protected virtual void Start()
        {

        }

        private void OnPreCullBiRP(Camera cam)
        {
            if (cam == Camera.main)
            {
                OnBeginMainCameraRendering(cam);
            }
        }

#if POSEIDON_2_URP
        private void OnBeginCameraRenderingURP(ScriptableRenderContext context, Camera cam)
        {
            if (cam == Camera.main)
            {
                OnBeginMainCameraRendering(cam);
            }
        }
#endif

        protected void OnBeginMainCameraRendering(Camera mainCamera)
        {
            UpdateEffects(mainCamera);

            Vector3 cameraPositionWS = mainCamera.transform.position;
            float waterHeightWS = GetWaterHeightWS(cameraPositionWS);
            float distanceToSuface = cameraPositionWS.y - waterHeightWS;
            if (distanceToSuface <= 0 && lastDistanceToSurface > 0)
            {
                if (waterBody.RenderersContainPointXZ(cameraPositionWS))
                {
                    m_onEnterWater.Invoke();
                }
            }
            if (distanceToSuface > 0 && lastDistanceToSurface <= 0)
            {
                if (waterBody.RenderersContainPointXZ(cameraPositionWS))
                {
                    m_onExitWater.Invoke();
                }
            }
            lastDistanceToSurface = distanceToSuface;
        }

        private void OnWaterShapeChanged(PoseidonWaterBody water)
        {
            if (water == m_waterBody)
            {
                SetupVolumes();
            }
        }

        private void UpdateEffects(Camera mainCamera)
        {
            float waterHeight = GetWaterHeightWS(mainCamera.transform.position);

            float underwaterIntensity = mainCamera.transform.position.y < waterHeight ? 1 : 0;
            UpdateUnderwaterFX(underwaterIntensity, waterHeight);


            float distanceToSurface = Camera.main.transform.position.y - waterHeight;

            if (distanceToSurface > 0 && lastDistanceToSurface <= 0)
            {
                wetLensTime = 0;
            }
            float wetlensIntensity;
            if (distanceToSurface <= 0)
            {
                wetlensIntensity = 0;
            }
            else if (wetLensTime > wetLensDuration)
            {
                wetlensIntensity = 0;
            }
            else
            {
                float f = Mathf.InverseLerp(0, wetLensDuration, wetLensTime);
                wetlensIntensity = wetLensFadeOutCurve.Evaluate(f);
            }
            UpdateWetLensFX(wetlensIntensity);

            wetLensTime += Time.deltaTime;
        }

        protected abstract void UpdateUnderwaterFX(float intensity, float waterLevelWS);
        protected abstract void UpdateWetLensFX(float intensity);

        private float GetWaterHeightWS(Vector3 positionWS)
        {
            float waterY = waterBody.transform.position.y;
            float offsetY = 0;

            if (waterBody.material != null)
            {
                if (PMat.HasWaveFX(waterBody.material))
                {
                    WaterUtilities.WaveParams waveParams = WaterUtilities.ExtractWaveParams(waterBody.material);
                    Vector3 waveOffset = WaterUtilities.CalculateWaveOffsetWS(positionWS, waveParams);

                    if (PMat.HasWaveMaskFX(waterBody.material))
                    {
                        WaterUtilities.WaveMaskParams waveMaskParams = WaterUtilities.ExtractWaveMaskParams(waterBody.material);
                        waveOffset = WaterUtilities.ApplyWaveMask(positionWS, waveOffset, waveMaskParams);
                    }

                    offsetY = waveOffset.y;
                }
            }

            return waterY + offsetY;
        }

        public const string VOLUMES_ROOT_NAME = "~Volume";
        [SerializeField]
        private List<GameObject> m_volumeObjects = new List<GameObject>();
        public IEnumerable<GameObject> volumeObjects => m_volumeObjects;

        public Transform GetOrAddVolumesRoot()
        {
            Transform root = transform.Find(VOLUMES_ROOT_NAME);
            if (root == null)
            {
                root = new GameObject(VOLUMES_ROOT_NAME).transform;
                root.SetParent(transform);
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;

                root.gameObject.layer = LayerMask.NameToLayer("Water");
            }
            return root;
        }

        public void SetupVolumes()
        {
            ClearVolumes();
            Transform root = GetOrAddVolumesRoot();

            List<Bounds> compositeBoundsWS = new List<Bounds>();
            waterBody.GenerateCompositeBoundingBoxes(compositeBoundsWS);
            for (int i = 0; i < compositeBoundsWS.Count; ++i)
            {
                Bounds bWS = compositeBoundsWS[i];
                Vector3 centerWS = bWS.center;
                Vector3 sizeWS = bWS.size;

                sizeWS += m_volumeExtent;

                GameObject go = new GameObject();
                go.transform.parent = root;
                go.transform.position = centerWS;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.layer = m_volumeLayer;
                go.name = $"~Volume {i}";

                BoxCollider collider = go.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = go.transform.InverseTransformVector(sizeWS);
                collider.center = Vector3.zero;

                AddVolumeComponentToObject(go);

                m_volumeObjects.Add(go);
            }
        }

        protected abstract void AddVolumeComponentToObject(GameObject go);

        public void ClearVolumes()
        {
            for (int i = 0; i < m_volumeObjects.Count; ++i)
            {
                GameObject v = m_volumeObjects[i];
                Object.DestroyImmediate(v);
            }
            m_volumeObjects.Clear();
        }

    }
}

#endif