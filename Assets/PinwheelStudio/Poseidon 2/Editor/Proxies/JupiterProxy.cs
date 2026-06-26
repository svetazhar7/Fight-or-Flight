#if POSEIDON_2
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Poseidon.JupiterPreview;
#if JUPITER
using Pinwheel.Jupiter;
#endif

namespace Pinwheel.Poseidon
{
    public static class JupiterProxy
    {
        [InitializeOnLoadMethod]
        private static void Init()
        {
            PlanarReflectionRendererInspector.IsJupiterInstalled += IsJupiterInstalled;
            PlanarReflectionRendererInspector.GetJupiterVersionString += GetJupiterVersionString;
            PlanarReflectionRendererInspector.GetJupiterSkyGameObjectInScene += GetJupiterGameObjectInScene;
            PlanarReflectionRendererInspector.AddJupiterSky += AddJupiterSky;
            PlanarReflectionRendererInspector.MatchJupiterSettingsWithPreview += MatchJupiterSettingsWithPreview;

            JupiterSkyPreviewInspector.IsJupiterInstalled += IsJupiterInstalled;
            JupiterSkyPreviewInspector.GetJupiterVersionString += GetJupiterVersionString;
            JupiterSkyPreviewInspector.GetJupiterSkyGameObjectInScene += GetJupiterGameObjectInScene;
            JupiterSkyPreviewInspector.AddJupiterSky += AddJupiterSky;
            JupiterSkyPreviewInspector.MatchJupiterSettingsWithPreview += MatchJupiterSettingsWithPreview;
        }

        public static bool IsJupiterInstalled()
        {
#if JUPITER
            return true;
#else
            return false;
#endif
        }

        public static string GetJupiterVersionString()
        {
#if JUPITER
            return JVersionInfo.Code;
#else
            return "";
#endif
        }

        public static GameObject GetJupiterGameObjectInScene()
        {
#if JUPITER
            JSky sky = Object.FindObjectOfType<JSky>();
            if (sky != null)
            {
                return sky.gameObject;
            }
            else
            {
                return null;
            }
#else
            return null;
#endif
        }

        public static GameObject AddJupiterSky()
        {
#if JUPITER
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

            JSky sky;
            if (maxLightIntensity >= 0.5f)
            {
                sky = JEditorMenus.CreateSunnyDaySky(null);
                sky.SunLightSource = dominantLight;
            }
            else
            {
                sky = JEditorMenus.CreateStarryNightSky(null);
                sky.MoonLightSource = dominantLight;
            }
            return sky.gameObject;
#else
            return null;
#endif
        }

        public static void MatchJupiterSettingsWithPreview(GameObject jupiterSky, JupiterSkyPreview preview)
        {
#if JUPITER
            JSky sky = jupiterSky.GetComponent<JSky>();
            if (sky == null)
                return;

            JSkyProfile skyProfile = sky.Profile;
            if (skyProfile == null)
                return;

            skyProfile.SkyColor = preview.SkyColor;
            skyProfile.HorizonColor = preview.HorizonColor;
            skyProfile.GroundColor = preview.GroundColor;
            skyProfile.HorizonThickness = preview.HorizonThickness;
            skyProfile.HorizonExponent = preview.HorizonExponent;

            if (preview.SunLightIntensity >= 0.5f)
            {
                skyProfile.EnableMoon = false;

                skyProfile.EnableSun = preview.EnableSun;
                skyProfile.SunColor = preview.SunColor;
                skyProfile.SunSize = preview.SunSize;
                skyProfile.SunSoftEdge = preview.SunSoftEdge;
                skyProfile.SunGlow = preview.SunGlow;
                skyProfile.SunLightColor = preview.SunLightColor;
                skyProfile.SunLightIntensity = preview.SunLightIntensity;

                sky.SunLightSource = preview.SunLightSource;
                sky.MoonLightSource = null;
            }
            else
            {
                skyProfile.EnableSun = false;

                skyProfile.EnableMoon = preview.EnableSun;
                skyProfile.MoonColor = preview.SunColor;
                skyProfile.MoonSize = preview.SunSize;
                skyProfile.MoonSoftEdge = preview.SunSoftEdge;
                skyProfile.MoonGlow = preview.SunGlow;
                skyProfile.MoonLightColor = preview.SunLightColor;
                skyProfile.MoonLightIntensity = preview.SunLightIntensity;

                sky.MoonLightSource = preview.SunLightSource;
                sky.SunLightSource = null;
            }

            skyProfile.EnableOverheadCloud = preview.EnableOverheadCloud;
            skyProfile.OverheadCloudColor = preview.OverheadCloudColor;
            skyProfile.OverheadCloudAltitude = 1000;
            skyProfile.OverheadCloudSize = 5;
            skyProfile.OverheadCloudStep = 1000;
            skyProfile.OverheadCloudAnimationSpeed = 20;
            skyProfile.OverheadCloudFlowSpeed = 1;
            skyProfile.OverheadCloudFlowDirectionX = 1;
            skyProfile.OverheadCloudFlowDirectionZ = 1;
            skyProfile.OverheadCloudRemapMin = 0;
            skyProfile.OverheadCloudRemapMax = 2;

            skyProfile.UpdateMaterialProperties();
#endif
        }
    }
}

#endif