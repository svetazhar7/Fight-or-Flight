using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace IslandSystem.Sun
{
    /// <summary>
    /// URP RenderGraph feature for <see cref="SunVolumetricModule"/> — ray-marched volumetric sun shafts. Runs
    /// AfterRenderingSkybox so the sky is present as the far backdrop and the shafts composite before post
    /// (bloom/grade pick them up). Reconstructs world position from the camera depth texture, marches the main
    /// light shadow map, composites additively. Requires the main light shadow map, so it needs shadows on.
    /// Skipped when the module is missing/disabled, faded out (no sun / cloud-covered), or on the no-post bake camera.
    /// </summary>
    public class SunVolumetricLightFeature : ScriptableRendererFeature
    {
        const string ShaderName = "Hidden/IslandSystem/SunVolumetricLight";

        Material _material;
        VolumetricPass _pass;

        public override void Create()
        {
            _pass = new VolumetricPass { renderPassEvent = RenderPassEvent.AfterRenderingSkybox };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null) return;
                _material = CoreUtils.CreateEngineMaterial(shader);
            }
            var module = SunVolumetricModule.Active;
            if (module == null || !module.isActiveAndEnabled || module.FinalIntensity <= 0.0001f) return;

            _pass.Setup(_material);
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        class VolumetricPass : ScriptableRenderPass
        {
            static readonly int ID_InvVP = Shader.PropertyToID("_VL_InvVP");
            static readonly int ID_Params = Shader.PropertyToID("_VL_Params");
            static readonly int ID_Params2 = Shader.PropertyToID("_VL_Params2");
            static readonly int ID_SunDir = Shader.PropertyToID("_VL_SunDir");
            static readonly int ID_Color = Shader.PropertyToID("_VL_Color");
            static readonly int ID_Texel = Shader.PropertyToID("_VL_Texel");

            Material _material;
            public void Setup(Material material) => _material = material;

            class PassData
            {
                public Material material;
                public TextureHandle source;
                public int shaderPass;
                public bool setParams, setTexel;
                public Matrix4x4 invVP;
                public Vector4 pParams, pParams2, sunDir, texel;
                public Color color;
            }

            static void Execute(PassData d, RasterGraphContext ctx)
            {
                if (d.setParams)
                {
                    ctx.cmd.SetGlobalMatrix(ID_InvVP, d.invVP);
                    ctx.cmd.SetGlobalVector(ID_Params, d.pParams);
                    ctx.cmd.SetGlobalVector(ID_Params2, d.pParams2);
                    ctx.cmd.SetGlobalVector(ID_SunDir, d.sunDir);
                    ctx.cmd.SetGlobalColor(ID_Color, d.color);
                }
                if (d.setTexel) ctx.cmd.SetGlobalVector(ID_Texel, d.texel);
                Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, d.shaderPass);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var module = SunVolumetricModule.Active;
                if (module == null || !module.isActiveAndEnabled || module.FinalIntensity <= 0.0001f || _material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView) return;
                if (!cameraData.postProcessEnabled) return;               // skip the impostor bake camera
                if (!resourceData.cameraDepthTexture.IsValid()) return;

                var cam = cameraData.camera;
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
                Matrix4x4 invVP = (gpuProj * cam.worldToCameraMatrix).inverse;

                var desc = cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.colorFormat = RenderTextureFormat.ARGBHalf;
                int ds = 1 << (int)module.downsample;
                desc.width = Mathf.Max(8, desc.width / ds);
                desc.height = Mathf.Max(8, desc.height / ds);

                var texel = new Vector4(1f / desc.width, 1f / desc.height, desc.width, desc.height);
                var pParams = new Vector4(module.steps, module.maxDistance, module.density, module.heightFalloff);
                var pParams2 = new Vector4(module.ambientScatter, module.anisotropy, module.groundHeight, module.FinalIntensity);
                Vector3 s = module.ToSunWorld;
                var sunDir = new Vector4(s.x, s.y, s.z, 1f);

                TextureHandle texA = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_SunVolA", false, FilterMode.Bilinear);
                TextureHandle texB = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_SunVolB", false, FilterMode.Bilinear);

                // ---- Pass 0: march (depth as blit source) ----
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sun Volumetric - March", out var d))
                {
                    d.material = _material;
                    d.shaderPass = 0;
                    d.source = resourceData.cameraDepthTexture;
                    d.setParams = true; d.setTexel = true;
                    d.invVP = invVP; d.pParams = pParams; d.pParams2 = pParams2; d.sunDir = sunDir; d.color = module.ShaftColor;
                    d.texel = texel;
                    builder.UseTexture(d.source);
                    builder.SetRenderAttachment(texA, 0);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc<PassData>(Execute);
                }

                // ---- Pass 1: blur ping-pong ----
                TextureHandle src = texA, dst = texB;
                int iterations = Mathf.Clamp(module.blurIterations, 0, 3);
                for (int i = 0; i < iterations; i++)
                {
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sun Volumetric - Blur", out var d))
                    {
                        d.material = _material;
                        d.shaderPass = 1;
                        d.source = src;
                        d.setParams = false; d.setTexel = false;
                        builder.UseTexture(d.source);
                        builder.SetRenderAttachment(dst, 0);
                        builder.SetRenderFunc<PassData>(Execute);
                    }
                    (src, dst) = (dst, src);
                }

                // ---- Pass 2: additive composite onto camera colour ----
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sun Volumetric - Composite", out var d))
                {
                    d.material = _material;
                    d.shaderPass = 2;
                    d.source = src;
                    d.setParams = false; d.setTexel = false;
                    builder.UseTexture(d.source);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    builder.SetRenderFunc<PassData>(Execute);
                }
            }
        }
    }
}
