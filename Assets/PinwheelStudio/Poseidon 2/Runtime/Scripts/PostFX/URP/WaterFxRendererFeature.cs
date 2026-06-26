#if POSEIDON_2
#if POSEIDON_2_URP
#if !UNITY_6000_0_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Pinwheel.Poseidon.FX.URP
{
    public class WaterFxRendererFeature : ScriptableRendererFeature
    {
        public Material m_underwaterMaterialerial;
        public Material wetLensMaterial;
        public bool enableUnderwaterCaustic;
        public bool enableUnderwaterDistortion;

        private WaterFxPass m_waterEffectPass;

        public override void Create()
        {
            if (m_underwaterMaterialerial == null)
            {
                m_underwaterMaterialerial = Resources.Load<Material>("Poseidon/Materials/UnderwaterURP");
            }
            if (wetLensMaterial == null)
            {
                wetLensMaterial = Resources.Load<Material>("Poseidon/Materials/WetLensURP");
            }
            enableUnderwaterCaustic = true;
            enableUnderwaterDistortion = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            m_waterEffectPass = new WaterFxPass(m_underwaterMaterialerial, wetLensMaterial, enableUnderwaterCaustic, enableUnderwaterDistortion);
            renderer.EnqueuePass(m_waterEffectPass);
        }

        public class WaterFxPass : ScriptableRenderPass
        {
            public const string PROFILER_TAG = "Water FX";

            protected Material m_underwaterMaterial;
            protected Material m_wetLensMaterial;
            protected bool m_enableUnderwaterCaustic;
            protected bool m_enableUnderwaterDistortion;

#if UNITY_2022_1_OR_NEWER
#pragma warning disable 0649
            private RTHandle cameraTarget = null;
            private RTHandle temporaryRenderTexture = null;
#pragma warning restore 0649
#else
            private RenderTargetIdentifier cameraTarget;
            private RenderTargetHandle temporaryRenderTexture;
#endif

            public WaterFxPass(Material underwaterMaterial, Material wetlensMaterial, bool underwaterCaustic, bool underwaterDistortion)
            {
                m_underwaterMaterial = underwaterMaterial;
                m_wetLensMaterial = wetlensMaterial;
                m_enableUnderwaterCaustic = underwaterCaustic;
                m_enableUnderwaterDistortion = underwaterDistortion;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {

            }

            private void ConfigureMaterial(ref RenderingData renderingData, UnderwaterUrp underwater, WetLensUrp wetLens)
            {
                if (underwater.intensity.value > 0)
                {
                    m_underwaterMaterial.SetFloat(PMat.PP_WATER_LEVEL, underwater.waterLevel.value);
                    m_underwaterMaterial.SetFloat(PMat.PP_MAX_DEPTH, underwater.maxDepth.value);
                    m_underwaterMaterial.SetFloat(PMat.PP_SURFACE_COLOR_BOOST, underwater.surfaceColorBoost.value);

                    m_underwaterMaterial.SetColor(PMat.PP_SHALLOW_FOG_COLOR, underwater.shallowFogColor.value);
                    m_underwaterMaterial.SetColor(PMat.PP_DEEP_FOG_COLOR, underwater.deepFogColor.value);
                    m_underwaterMaterial.SetFloat(PMat.PP_VIEW_DISTANCE, underwater.viewDistance.value);

                    if (m_enableUnderwaterCaustic)
                    {
                        m_underwaterMaterial.EnableKeyword(PMat.KW_PP_CAUSTIC);
                        m_underwaterMaterial.SetTexture(PMat.PP_CAUSTIC_TEX, underwater.causticTexture.value);
                        m_underwaterMaterial.SetFloat(PMat.PP_CAUSTIC_SIZE, underwater.causticSize.value);
                        m_underwaterMaterial.SetFloat(PMat.PP_CAUSTIC_STRENGTH, underwater.causticStrength.value);
                    }
                    else
                    {
                        m_underwaterMaterial.DisableKeyword(PMat.KW_PP_CAUSTIC);
                    }

                    if (m_enableUnderwaterDistortion)
                    {
                        m_underwaterMaterial.EnableKeyword(PMat.KW_PP_DISTORTION);
                        m_underwaterMaterial.SetTexture(PMat.PP_DISTORTION_TEX, underwater.distortionNormalMap.value);
                        m_underwaterMaterial.SetFloat(PMat.PP_DISTORTION_STRENGTH, underwater.distortionStrength.value);
                        m_underwaterMaterial.SetFloat(PMat.PP_WATER_FLOW_SPEED, underwater.waterFlowSpeed.value);
                    }
                    else
                    {
                        m_underwaterMaterial.DisableKeyword(PMat.KW_PP_DISTORTION);
                    }

                    m_underwaterMaterial.SetTexture(PMat.PP_NOISE_TEX, underwater.noiseTexture.value);
                    m_underwaterMaterial.SetVector(PMat.PP_CAMERA_VIEW_DIR, renderingData.cameraData.camera.transform.forward);
                    m_underwaterMaterial.SetFloat(PMat.PP_CAMERA_FOV, renderingData.cameraData.camera.fieldOfView);
                    m_underwaterMaterial.SetMatrix(PMat.PP_CAMERA_TO_WORLD_MATRIX, renderingData.cameraData.camera.cameraToWorldMatrix);
                    m_underwaterMaterial.SetFloat(PMat.PP_INTENSITY, underwater.intensity.value);
                }

                if (wetLens.strength.value * wetLens.intensity.value > 0)
                {
                    m_wetLensMaterial.SetTexture(PMat.PP_WET_LENS_TEX, wetLens.dropletsNormalMap.value);
                    m_wetLensMaterial.SetFloat(PMat.PP_WET_LENS_STRENGTH, wetLens.strength.value * wetLens.intensity.value);
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (renderingData.cameraData.camera != Camera.main)
                    return;

                VolumeStack stack = VolumeManager.instance.stack;
                UnderwaterUrp underwater = stack.GetComponent<UnderwaterUrp>();
                WetLensUrp wetLens = stack.GetComponent<WetLensUrp>();

                bool willRenderUnderwater = underwater.intensity.value > 0;
                bool willRenderWetLens = wetLens.strength.value * wetLens.intensity.value > 0;
                if (!willRenderUnderwater && !willRenderWetLens)
                    return;

                ConfigureMaterial(ref renderingData, underwater, wetLens);

                Material material = willRenderUnderwater ? m_underwaterMaterial : m_wetLensMaterial;
                CommandBuffer cmd = CommandBufferPool.Get(PROFILER_TAG);
                RenderTextureDescriptor cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                cameraTargetDescriptor.depthBufferBits = 0;

#if UNITY_2022_1_OR_NEWER
                cameraTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                temporaryRenderTexture = RTHandles.Alloc(cameraTargetDescriptor);
                material.SetTexture(PMat.MAIN_TEX, cameraTarget);
                Blit(cmd, cameraTarget, temporaryRenderTexture, material, 0);
                Blit(cmd, temporaryRenderTexture, cameraTarget);
#elif UNITY_2021_2_OR_NEWER
                cameraTarget = renderingData.cameraData.renderer.cameraColorTarget;
                cmd.GetTemporaryRT(temporaryRenderTexture.id, cameraTargetDescriptor);
                Blit(cmd, cameraTarget, temporaryRenderTexture.Identifier(), material, 0);
                Blit(cmd, temporaryRenderTexture.Identifier(), cameraTarget);
#else
                cameraTarget = UniversalRenderPipeline.asset.scriptableRenderer.cameraColorTarget;
                cmd.GetTemporaryRT(temporaryRenderTexture.id, cameraTargetDescriptor);
                Blit(cmd, cameraTarget, temporaryRenderTexture.Identifier(), material, 0);
                Blit(cmd, temporaryRenderTexture.Identifier(), cameraTarget);
#endif

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public override void FrameCleanup(CommandBuffer cmd)
            {
#if UNITY_2022_1_OR_NEWER
                RTHandles.Release(temporaryRenderTexture);
#else
                cmd.ReleaseTemporaryRT(temporaryRenderTexture.id);
#endif
            }
        }
    }
}
#else
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Pinwheel.Poseidon.FX.URP
{
    public sealed class WaterFxRendererFeature : ScriptableRendererFeature
    {
        public Material underwaterMaterial;
        public Material wetLensMaterial;
        public bool enableUnderwaterCaustic;
        public bool enableUnderwaterDistortion;

        private WaterFxPass m_waterEffectPass;

        public override void Create()
        {
            if (underwaterMaterial == null)
            {
                underwaterMaterial = Resources.Load<Material>("Poseidon/Materials/UnderwaterURP");
            }
            if (wetLensMaterial == null)
            {
                wetLensMaterial = Resources.Load<Material>("Poseidon/Materials/WetLensURP");
            }
            enableUnderwaterCaustic = true;
            enableUnderwaterDistortion = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (underwaterMaterial == null ||
                wetLensMaterial == null)
                return;

            if (renderingData.cameraData.cameraType != CameraType.Game)
                return;

            m_waterEffectPass = new WaterFxPass(underwaterMaterial, wetLensMaterial, enableUnderwaterCaustic, enableUnderwaterDistortion);

            m_waterEffectPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            m_waterEffectPass.ConfigureInput(ScriptableRenderPassInput.Depth);

            renderer.EnqueuePass(m_waterEffectPass);
        }

        protected override void Dispose(bool disposing)
        {

        }

        public class WaterFxPass : ScriptableRenderPass
        {
            public const string PROFILER_TAG = "Water FX";

            protected Material m_underwaterMaterial;
            protected Material m_wetLensMaterial;
            public bool m_enableUnderwaterCaustic;
            public bool m_enableUnderwaterDistortion;

            private static MaterialPropertyBlock s_SharedPropertyBlock = new MaterialPropertyBlock();
            private static readonly int kBlitTexturePropertyId = Shader.PropertyToID("_BlitTexture");
            private static readonly int kBlitScaleBiasPropertyId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int kIntensityPropertyId = Shader.PropertyToID("_Intensity");

            private RTHandle m_CopiedColor;

            public WaterFxPass(Material underwaterMaterial, Material wetlensMaterial, bool underwaterCaustic, bool underwaterDistortion)
            {
                m_underwaterMaterial = underwaterMaterial;
                m_wetLensMaterial = wetlensMaterial;
                m_enableUnderwaterCaustic = underwaterCaustic;
                m_enableUnderwaterDistortion = underwaterDistortion;

                profilingSampler = new ProfilingSampler(passName);
                requiresIntermediateTexture = true;
            }

#region PASS_RENDER_GRAPH_PATH
            private class PassData
            {
                public Material underwaterMaterialerial;
                public Material wetLensMaterial;
                public TextureHandle inputTexture;
                public Vector3 cameraForward;
                public float cameraFov;
                public Matrix4x4 cameraToWorldMatrix;

                public bool enableUnderwaterCaustic;
                public bool enableUnderwaterDistortion;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                VolumeStack stack = VolumeManager.instance.stack;
                UnderwaterUrp underwater = stack.GetComponent<UnderwaterUrp>();
                WetLensUrp wetLens = stack.GetComponent<WetLensUrp>();

                bool willRenderUnderwater = underwater.intensity.value > 0;
                bool willRenderWetLens = wetLens.strength.value * wetLens.intensity.value > 0;
                if (!willRenderUnderwater && !willRenderWetLens)
                    return;

                UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                using (var builder = renderGraph.AddRasterRenderPass<WaterFxPass.PassData>(passName, out var passData, profilingSampler))
                {
                    passData.enableUnderwaterCaustic = m_enableUnderwaterCaustic;
                    passData.enableUnderwaterDistortion = m_enableUnderwaterDistortion;

                    passData.underwaterMaterialerial = this.m_underwaterMaterial;
                    passData.wetLensMaterial = this.m_wetLensMaterial;
                    passData.cameraForward = cameraData.camera.transform.forward;
                    passData.cameraFov = cameraData.camera.fieldOfView;
                    passData.cameraToWorldMatrix = cameraData.camera.cameraToWorldMatrix;

                    var cameraColorDesc = renderGraph.GetTextureDesc(resourcesData.cameraColor);
                    cameraColorDesc.name = "_CameraColor_WaterFX";
                    cameraColorDesc.clearBuffer = false;

                    TextureHandle destination = renderGraph.CreateTexture(cameraColorDesc);
                    passData.inputTexture = resourcesData.cameraColor;

                    builder.UseTexture(passData.inputTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc((WaterFxPass.PassData data, RasterGraphContext context) => ExecuteMainPass(data, context));

                    //Swap cameraColor to the new temp resource (destination) for the next pass
                    resourcesData.cameraColor = destination;
                }
            }

            private static void ExecuteMainPass(WaterFxPass.PassData data, RasterGraphContext context)
            {
                ExecuteMainPass(context.cmd, data.inputTexture.IsValid() ? data.inputTexture : null, data);
            }
#endregion

#region PASS_NON_RENDER_GRAPH_PATH
// Excluded on Unity 6 / URP 17: the non-RenderGraph compatibility path uses APIs removed in URP 17
// (OnCameraSetup/Execute overrides, ResetTarget, cameraColorTargetHandle). The RenderGraph path above
// handles rendering on Unity 6. (Project-local fix for Poseidon 2 on URP 17.)
#if !UNITY_6000_0_OR_NEWER
            [System.Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
            public void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ResetTarget();
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_CopiedColor, GetCopyPassTextureDescriptor(renderingData.cameraData.cameraTargetDescriptor), name: "_WaterFXPassCopyColor");
            }

            private static RenderTextureDescriptor GetCopyPassTextureDescriptor(RenderTextureDescriptor desc)
            {
                desc.msaaSamples = 1;
                desc.depthBufferBits = (int)DepthBits.None;

                return desc;
            }

            [System.Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
            public void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                VolumeStack stack = VolumeManager.instance.stack;
                UnderwaterUrp underwater = stack.GetComponent<UnderwaterUrp>();
                WetLensUrp wetLens = stack.GetComponent<WetLensUrp>();

                bool willRenderUnderwater = underwater.intensity.value > 0;
                bool willRenderWetLens = wetLens.strength.value * wetLens.intensity.value > 0;
                if (!willRenderUnderwater && !willRenderWetLens)
                    return;

                ref var cameraData = ref renderingData.cameraData;
                var cmd = CommandBufferPool.Get();

                using (new ProfilingScope(cmd, profilingSampler))
                {
                    PassData passData = new PassData();
                    passData.underwaterMaterialerial = this.m_underwaterMaterial;
                    passData.wetLensMaterial = this.m_wetLensMaterial;
                    passData.cameraForward = cameraData.camera.transform.forward;
                    passData.cameraFov = cameraData.camera.fieldOfView;
                    passData.cameraToWorldMatrix = cameraData.camera.cameraToWorldMatrix;

                    RasterCommandBuffer rasterCmd = CommandBufferHelpers.GetRasterCommandBuffer(cmd);

                    CoreUtils.SetRenderTarget(cmd, m_CopiedColor);
                    ExecuteCopyColorPass(rasterCmd, cameraData.renderer.cameraColorTargetHandle);

                    CoreUtils.SetRenderTarget(cmd, cameraData.renderer.cameraColorTargetHandle, cameraData.renderer.cameraDepthTargetHandle);

                    ExecuteMainPass(rasterCmd, m_CopiedColor, passData);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                CommandBufferPool.Release(cmd);
            }

            private static void ExecuteCopyColorPass(RasterCommandBuffer cmd, RTHandle sourceTexture)
            {
                Blitter.BlitTexture(cmd, sourceTexture, new Vector4(1, 1, 0, 0), 0.0f, false);
            }
#endif
#endregion

#region PASS_SHARED_RENDERING_CODE
            private static void ExecuteMainPass(RasterCommandBuffer cmd, RTHandle sourceTexture, PassData passData)
            {
                s_SharedPropertyBlock.Clear();
                if (sourceTexture != null)
                    s_SharedPropertyBlock.SetTexture(kBlitTexturePropertyId, sourceTexture);

                // This uniform needs to be set for user materials with shaders relying on core Blit.hlsl to work as expected
                s_SharedPropertyBlock.SetVector(kBlitScaleBiasPropertyId, new Vector4(1, 1, 0, 0));

                //The shader use _MainTex for copied screen color
                s_SharedPropertyBlock.SetTexture(PMat.MAIN_TEX, sourceTexture);

                VolumeStack stack = VolumeManager.instance.stack;
                UnderwaterUrp underwater = stack.GetComponent<UnderwaterUrp>();
                WetLensUrp wetLens = stack.GetComponent<WetLensUrp>();

                bool willRenderUnderwater = underwater.intensity.value > 0;
                bool willRenderWetLens = wetLens.strength.value * wetLens.intensity.value > 0;
                if (!willRenderUnderwater && !willRenderWetLens)
                    return;

                Material underwaterMaterial = passData.underwaterMaterialerial;
                Material wetLensMaterial = passData.wetLensMaterial;
                Material materialToRender = null;

                if (willRenderUnderwater)
                {
                    s_SharedPropertyBlock.SetFloat(PMat.PP_WATER_LEVEL, underwater.waterLevel.value);
                    s_SharedPropertyBlock.SetFloat(PMat.PP_MAX_DEPTH, underwater.maxDepth.value);
                    s_SharedPropertyBlock.SetFloat(PMat.PP_SURFACE_COLOR_BOOST, underwater.surfaceColorBoost.value);

                    s_SharedPropertyBlock.SetColor(PMat.PP_SHALLOW_FOG_COLOR, underwater.shallowFogColor.value);
                    s_SharedPropertyBlock.SetColor(PMat.PP_DEEP_FOG_COLOR, underwater.deepFogColor.value);
                    s_SharedPropertyBlock.SetFloat(PMat.PP_VIEW_DISTANCE, underwater.viewDistance.value);

                    if (passData.enableUnderwaterCaustic)
                    {
                        underwaterMaterial.EnableKeyword(PMat.KW_PP_CAUSTIC);
                        s_SharedPropertyBlock.SetTexture(PMat.PP_CAUSTIC_TEX, underwater.causticTexture.value);
                        s_SharedPropertyBlock.SetFloat(PMat.PP_CAUSTIC_SIZE, underwater.causticSize.value);
                        s_SharedPropertyBlock.SetFloat(PMat.PP_CAUSTIC_STRENGTH, underwater.causticStrength.value);
                    }
                    else
                    {
                        underwaterMaterial.DisableKeyword(PMat.KW_PP_CAUSTIC);
                    }

                    if (passData.enableUnderwaterDistortion)
                    {
                        underwaterMaterial.EnableKeyword(PMat.KW_PP_DISTORTION);
                        s_SharedPropertyBlock.SetTexture(PMat.PP_DISTORTION_TEX, underwater.distortionNormalMap.value);
                        s_SharedPropertyBlock.SetFloat(PMat.PP_DISTORTION_STRENGTH, underwater.distortionStrength.value);
                        s_SharedPropertyBlock.SetFloat(PMat.PP_WATER_FLOW_SPEED, underwater.waterFlowSpeed.value);
                    }
                    else
                    {
                        underwaterMaterial.DisableKeyword(PMat.KW_PP_DISTORTION);
                    }

                    s_SharedPropertyBlock.SetTexture(PMat.PP_NOISE_TEX, underwater.noiseTexture.value);
                    s_SharedPropertyBlock.SetVector(PMat.PP_CAMERA_VIEW_DIR, passData.cameraForward);
                    s_SharedPropertyBlock.SetFloat(PMat.PP_CAMERA_FOV, passData.cameraFov);
                    s_SharedPropertyBlock.SetMatrix(PMat.PP_CAMERA_TO_WORLD_MATRIX, passData.cameraToWorldMatrix);
                    s_SharedPropertyBlock.SetFloat(PMat.PP_INTENSITY, underwater.intensity.value);

                    materialToRender = underwaterMaterial;
                }

                if (willRenderWetLens)
                {
                    s_SharedPropertyBlock.SetTexture(PMat.PP_WET_LENS_TEX, wetLens.dropletsNormalMap.value);
                    s_SharedPropertyBlock.SetFloat(PMat.PP_WET_LENS_STRENGTH, wetLens.strength.value * wetLens.intensity.value);

                    materialToRender = wetLensMaterial;
                }

                if (materialToRender)
                {
                    cmd.DrawProcedural(Matrix4x4.identity, materialToRender, 0, MeshTopology.Triangles, 3, 1, s_SharedPropertyBlock);
                }
            }
#endregion
        }
    }
}
#endif
#endif  
#endif