#if POSEIDON_2
#if POSEIDON_2_URP
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;
#if UNITY_POST_PROCESSING_STACK_V2
using UnityEngine.Rendering.PostProcessing;
#endif

namespace Pinwheel.Poseidon.FX.BiRP
{
    [CustomEditor(typeof(WaterFxControllerBirp))]
    public class WaterFxControllerBirpInspector
#if UNITY_POST_PROCESSING_STACK_V2
        : WaterFxControllerBaseInspector
    {
        protected override void DrawConfigurationCheck()
        {
            if (PCommon.CurrentRenderPipeline != RenderPipelineType.Builtin)
            {
                EditorGUILayout.HelpBox($"This component only work in BiRP. For URP, please use {typeof(URP.WaterFxControllerUrp).Name} instead.", MessageType.Warning);
            }
            if (Camera.main != null)
            {
                PostProcessLayer ppLayer = Camera.main.GetComponent<PostProcessLayer>();
                if (ppLayer == null)
                {
                    EditorGUILayout.HelpBox($"Looks like your main camera '{Camera.main.name}' doesn't have {nameof(PostProcessLayer)} attached.", MessageType.Info);
                }
                else if (!ppLayer.isActiveAndEnabled)
                {
                    EditorGUILayout.HelpBox($"The {nameof(PostProcessLayer)} component on your main camera '{Camera.main.name}' was disabled.", MessageType.Info);
                }
                else
                {
                    if ((ppLayer.volumeLayer.value & LayerMask.GetMask(LayerMask.LayerToName(m_instance.volumeLayer))) == 0)
                    {
                        EditorGUILayout.HelpBox($"Layer mismatched between {nameof(PostProcessLayer)} and {nameof(WaterFxControllerBirp)}", MessageType.Info);
                    }
                }
            }
        }

        protected override void DrawVolumeProfileField()
        {
            WaterFxControllerBirp controller = m_instance as WaterFxControllerBirp;

            EditorGUI.BeginChangeCheck();
            PostProcessProfile profile = EditorGUILayout.ObjectField("Profile", controller.postProcessProfile, typeof(PostProcessProfile), false) as PostProcessProfile;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, $"Modify {controller.name}");
                EditorUtility.SetDirty(controller);
                controller.postProcessProfile = profile;
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
            WaterFxControllerBirp controller = m_instance as WaterFxControllerBirp;
            Material mat = controller.waterBody.material;
            PostProcessProfile volume = controller.postProcessProfile;
            if (mat == null || volume == null)
                return;

            Debug.Log($"Copying material properties from {controller.waterBody.material.name} to volume {controller.postProcessProfile.name}");

            UnderwaterBirp underwater;
            if (volume.TryGetSettings(out underwater))
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
#else
    :Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Please install Post Processing Stack V2 (com.unity.postprocessing) using the Package Manager to use this component.", MessageType.Info);
        }
    }
#endif
}
#endif  
#endif