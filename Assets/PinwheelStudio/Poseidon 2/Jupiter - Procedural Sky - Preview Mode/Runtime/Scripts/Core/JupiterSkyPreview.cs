#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.Rendering;
using StopWatch = System.Diagnostics.Stopwatch;

namespace Pinwheel.Poseidon.JupiterPreview
{
    [ExecuteInEditMode]
    public class JupiterSkyPreview : MonoBehaviour
    {
        public static readonly Vector3 DefaultSunDirection = Vector3.forward;

        [SerializeField]
        private Light sunLightSource;
        public Light SunLightSource
        {
            get
            {
                return sunLightSource;
            }
            set
            {
                Light src = value;
                if (src != null && src.type == LightType.Directional)
                {
                    sunLightSource = src;
                }
                else
                {
                    sunLightSource = null;
                }
            }
        }

        [SerializeField]
        private Color skyColor;
        public Color SkyColor
        {
            get
            {
                return skyColor;
            }
            set
            {
                skyColor = value;
            }
        }

        [SerializeField]
        private Color horizonColor;
        public Color HorizonColor
        {
            get
            {
                return horizonColor;
            }
            set
            {
                horizonColor = value;
            }
        }

        [SerializeField]
        private Color groundColor;
        public Color GroundColor
        {
            get
            {
                return groundColor;
            }
            set
            {
                groundColor = value;
            }
        }

        [SerializeField]
        private float horizonThickness;
        public float HorizonThickness
        {
            get
            {
                return horizonThickness;
            }
            set
            {
                horizonThickness = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        private float horizonExponent;
        public float HorizonExponent
        {
            get
            {
                return horizonExponent;
            }
            set
            {
                horizonExponent = Mathf.Max(0.01f, value);
            }
        }

        [SerializeField]
        private bool enableSun;
        public bool EnableSun
        {
            get
            {
                return enableSun;
            }
            set
            {
                enableSun = value;
            }
        }

        [SerializeField]
        private Color sunColor;
        public Color SunColor
        {
            get
            {
                return sunColor;
            }
            set
            {
                sunColor = value;
            }
        }

        [SerializeField]
        private float sunSize;
        public float SunSize
        {
            get
            {
                return sunSize;
            }
            set
            {
                sunSize = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        private float sunSoftEdge;
        public float SunSoftEdge
        {
            get
            {
                return sunSoftEdge;
            }
            set
            {
                sunSoftEdge = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        private float sunGlow;
        public float SunGlow
        {
            get
            {
                return sunGlow;
            }
            set
            {
                sunGlow = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        private Color sunLightColor;
        public Color SunLightColor
        {
            get
            {
                return sunLightColor;
            }
            set
            {
                sunLightColor = value;
            }
        }

        [SerializeField]
        private float sunLightIntensity;
        public float SunLightIntensity
        {
            get
            {
                return sunLightIntensity;
            }
            set
            {
                sunLightIntensity = value;
            }
        }

        [SerializeField]
        private bool enableOverheadCloud;
        public bool EnableOverheadCloud
        {
            get
            {
                return enableOverheadCloud;
            }
            set
            {
                enableOverheadCloud = value;
            }
        }

        [SerializeField]
        private Color overheadCloudColor;
        public Color OverheadCloudColor => overheadCloudColor;

        [SerializeField]
        private Shader skyShader;
        [SerializeField]
        private Material defaultSkybox;
        [SerializeField]
        private Texture2D noiseTexture;
        [SerializeField]
        private Texture2D cloudTexture;



        [SerializeField]
        private Material material;
        public Material Material
        {
            get
            {
                if (material == null)
                {
                    material = new Material(skyShader);
                }

                material.name = material.shader.name;
                return material;
            }
        }

        private StopWatch timer;
        private double lastElapsedSeconds;
        private double frameTime;

        private void OnEnable()
        {
            Camera.onPreCull += OnCameraPreCull;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRenderingSRP;

            timer = new StopWatch();
            timer.Start();
        }

        private void OnDisable()
        {
            Camera.onPreCull -= OnCameraPreCull;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRenderingSRP;
            RenderSettings.skybox = defaultSkybox;
        }

        private void OnDestroy()
        {
            RenderSettings.skybox = defaultSkybox;
        }

        private void Reset()
        {
            ApplyPresetBasedOnDominantLight();
        }

        private void OnCameraPreCull(Camera cam)
        {
            SetupSkyMaterial();
        }

        private void OnBeginCameraRenderingSRP(ScriptableRenderContext context, Camera cam)
        {
            OnCameraPreCull(cam);
        }

        public double GetTimeParam()
        {
            if (timer == null)
                return 0;
            double elapsedSeconds = timer.ElapsedMilliseconds * 0.001f;
            double delta = elapsedSeconds - lastElapsedSeconds;
            frameTime += delta * Time.timeScale;
            lastElapsedSeconds = elapsedSeconds;
            return frameTime;
        }

        private void SetupSkyMaterial()
        {
            RenderSettings.skybox = Material;

            Shader.SetGlobalFloat(JMat.TIME, (float)GetTimeParam());
            Material.SetTexture(JMat.NOISE_TEX, noiseTexture);
            Material.SetTexture(JMat.CLOUD_TEX, cloudTexture);

            if (EnableSun)
            {
                if (SunLightSource != null)
                {
                    bool isSunLightColorOverridden = false;
                    if (!isSunLightColorOverridden)
                    {
                        SunLightSource.color = SunLightColor;
                    }
                    bool isSunLightIntensityOverridden = false;
                    if (!isSunLightIntensityOverridden)
                    {
                        SunLightSource.intensity = SunLightIntensity;
                    }
                }

                Vector3 sunDirection = SunLightSource ? SunLightSource.transform.forward : DefaultSunDirection;
                Matrix4x4 positionToSunUV = Matrix4x4.TRS(
                    -sunDirection,
                    Quaternion.LookRotation(sunDirection),
                    SunSize * Vector3.one).inverse;
                Material.SetVector(JMat.SUN_DIRECTION, sunDirection);
                Material.SetMatrix(JMat.SUN_TRANSFORM_MATRIX, positionToSunUV);
            }

            UpdateMaterialProperties(Material);
        }

        public void UpdateMaterialProperties(Material mat)
        {
            JMat.SetActiveMaterial(mat);

            JMat.SetColor(JMat.SKY_COLOR, SkyColor);
            JMat.SetColor(JMat.HORIZON_COLOR, HorizonColor);
            JMat.SetColor(JMat.GROUND_COLOR, GroundColor);
            JMat.SetFloat(JMat.HORIZON_THICKNESS, HorizonThickness);
            JMat.SetFloat(JMat.HORIZON_EXPONENT, HorizonExponent);

            JMat.SetKeywordEnable(JMat.KW_SUN, EnableSun);
            JMat.SetColor(JMat.SUN_COLOR, SunColor);
            JMat.SetFloat(JMat.SUN_SIZE, SunSize);
            JMat.SetFloat(JMat.SUN_SOFT_EDGE, SunSoftEdge);
            JMat.SetFloat(JMat.SUN_GLOW, SunGlow);
            JMat.SetColor(JMat.SUN_LIGHT_COLOR, SunLightColor);
            JMat.SetFloat(JMat.SUN_LIGHT_INTENSITY, SunLightIntensity);

            JMat.SetKeywordEnable(JMat.KW_OVERHEAD_CLOUD, EnableOverheadCloud);
            JMat.SetColor(JMat.OVERHEAD_CLOUD_COLOR, overheadCloudColor);

            JMat.SetActiveMaterial(null);
        }

        public void ApplyDayPreset()
        {
            skyColor = new Color32(86, 123, 176, 255);
            horizonColor = new Color32(198, 238, 255, 255);
            groundColor = new Color32(130, 130, 130, 255);
            horizonExponent = 2;
            horizonThickness = 1f;

            enableSun = true;
            sunColor = new Color(191f / 255f, 165f / 255f, 72f / 255f, 255f) * 3.4f;
            sunSize = 0.02f;
            sunSoftEdge = 0.05f;
            sunGlow = 0.1f;
            sunLightColor = new Color32(255, 248, 225, 255);
            sunLightIntensity = 1.2f;

            enableOverheadCloud = true;
            overheadCloudColor = new Color(1, 1, 1, 1);
        }

        public void ApplyNightPreset()
        {
            skyColor = new Color32(34, 28, 89, 255);
            horizonColor = new Color32(89, 76, 116, 255);
            groundColor = new Color32(70, 70, 70, 255);
            horizonExponent = 2;
            horizonThickness = 0.5f;

            enableSun = true;
            sunColor = new Color(1, 1, 1, 1) * 3.2f;
            sunSize = 0.015f;
            sunSoftEdge = 0.05f;
            sunGlow = 0.1f;
            sunLightColor = new Color32(255, 255, 225, 255);
            sunLightIntensity = 0.2f;

            enableOverheadCloud = true;
            overheadCloudColor = new Color(1, 1, 1, 0.25f);
        }

        public void ApplyPresetBasedOnDominantLight()
        {
            float maxLightIntensity = 0;
            Light dominantLight = null;
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; ++i)
            {
                if (lights[i].type == LightType.Directional && lights[i].intensity > maxLightIntensity)
                {
                    dominantLight = lights[i];
                    maxLightIntensity = lights[i].intensity;
                    break;
                }
            }
            SunLightSource = dominantLight;

            if (maxLightIntensity >= 0.5f)
            {
                ApplyDayPreset();
            }
            else
            {
                ApplyNightPreset();
            }
        }
    }
}

#endif