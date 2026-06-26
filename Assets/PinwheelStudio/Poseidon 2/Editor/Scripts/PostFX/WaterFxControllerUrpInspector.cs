#if POSEIDON_2
#if POSEIDON_2_URP
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;
using Pinwheel.Poseidon.FX.URP;
using UnityEngine.Rendering.Universal;
using System.Reflection;

namespace Pinwheel.Poseidon.FX.URP
{
    [CustomEditor(typeof(WaterFxControllerUrp))]
    public class WaterFxControllerUrpInspector : WaterFxControllerBaseInspector
    {
        protected override void DrawConfigurationCheck()
        {
            if (PCommon.CurrentRenderPipeline != RenderPipelineType.Universal)
            {
                EditorGUILayout.HelpBox($"This component only work in URP. For BiRP, please use {typeof(BiRP.WaterFxControllerBirp).Name} instead.", MessageType.Warning);
            }

            RendererFeatureSetupCode rfCode = CheckRendererFeatureConfig();
            switch (rfCode)
            {
                case RendererFeatureSetupCode.NotAdded:
                    EditorGUILayout.HelpBox($"Please add a {typeof(WaterFxRendererFeature).Name} to your current URP asset {UniversalRenderPipeline.asset.name}", MessageType.Info);
                    break;
                case RendererFeatureSetupCode.Disabled:
                    EditorGUILayout.HelpBox($"Please enable the {typeof(WaterFxRendererFeature).Name} to your current URP asset {UniversalRenderPipeline.asset.name}", MessageType.Info);
                    break;
            }
        }

        enum RendererFeatureSetupCode
        {
            Ok, NotAdded, Disabled, NotUrp
        }

        private RendererFeatureSetupCode CheckRendererFeatureConfig()
        {
            UniversalRenderPipelineAsset uAsset = UniversalRenderPipeline.asset;
            if (uAsset == null)
                return RendererFeatureSetupCode.NotUrp;

            ScriptableRenderer renderer = uAsset.scriptableRenderer;
            PropertyInfo rendererFeaturesProperty = renderer.GetType().GetProperty("rendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic);
            if (rendererFeaturesProperty != null)
            {
                List<ScriptableRendererFeature> rendererFeatures = rendererFeaturesProperty.GetValue(renderer) as List<ScriptableRendererFeature>;
                if (rendererFeatures != null)
                {
                    RendererFeatureSetupCode code = RendererFeatureSetupCode.NotAdded;
                    for (int i = 0; i < rendererFeatures.Count; ++i)
                    {
                        if (rendererFeatures[i] is WaterFxRendererFeature rf)
                        {
                            code = rf.isActive ? RendererFeatureSetupCode.Ok : RendererFeatureSetupCode.Disabled;
                        }
                    }
                    return code;
                }
            }
            return RendererFeatureSetupCode.Ok;
        }

        protected override void DrawVolumeProfileField()
        {
            WaterFxControllerUrp controller = m_instance as WaterFxControllerUrp;

            EditorGUI.BeginChangeCheck();
            VolumeProfile profile = EditorGUILayout.ObjectField("Profile", controller.volumeProfile, typeof(VolumeProfile), false) as VolumeProfile;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, $"Modify {controller.name}");
                EditorUtility.SetDirty(controller);
                controller.volumeProfile = profile;
                controller.SetupVolumes();
            }

            GUI.enabled = controller.waterBody.material != null && profile != null;
            if (GUILayout.Button("Inherit values from water material"))
            {
                CopyValuesFromMaterialToVolume();
            }
            GUI.enabled = true;
        }

        protected void CopyValuesFromMaterialToVolume()
        {
            WaterFxControllerUrp controller = m_instance as WaterFxControllerUrp;
            Material mat = controller.waterBody.material;
            VolumeProfile volume = controller.volumeProfile;
            if (mat == null || volume == null)
                return;

            Debug.Log($"Copying material properties from {controller.waterBody.material.name} to volume {controller.volumeProfile.name}");

            UnderwaterUrp underwater;
            if (volume.TryGet(out underwater))
            {
                if (mat.HasProperty(PMat.MAX_DEPTH))
                {
                    underwater.maxDepth.value = mat.GetFloat(PMat.MAX_DEPTH);
                }
                if (mat.HasProperty(PMat.COLOR))
                {
                    underwater.shallowFogColor.value = mat.GetColor(PMat.COLOR);
                }
                if (mat.HasProperty(PMat.DEPTH_COLOR))
                {
                    underwater.deepFogColor.value = mat.GetColor(PMat.DEPTH_COLOR);
                }
                if (mat.HasProperty(PMat.CAUSTIC_TEX))
                {
                    underwater.causticTexture.value = mat.GetTexture(PMat.CAUSTIC_TEX);
                }
                if (mat.HasProperty(PMat.NOISE_TEX))
                {
                    underwater.noiseTexture.value = mat.GetTexture(PMat.NOISE_TEX);
                }
                if (mat.HasFloat(PMat.CAUSTIC_SIZE))
                {
                    underwater.causticSize.value = mat.GetFloat(PMat.CAUSTIC_SIZE);
                }
                if (mat.HasFloat(PMat.CAUSTIC_STRENGTH))
                {
                    underwater.causticStrength.value = mat.GetFloat(PMat.CAUSTIC_STRENGTH);
                }
            }
        }
    }
}
#endif  
#endif