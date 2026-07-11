using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace IslandSystem.Sun
{
    /// <summary>
    /// URP RenderGraph feature for the screen-space god rays. Three raster passes at reduced resolution:
    ///   0) occlusion mask — sky pixels (from the camera depth texture) weighted by distance to the sun's screen
    ///      position, carved by everything that writes depth and shaped by the angular streak noise;
    ///   1) radial blur ping-pong (1..3 iterations) smearing the mask away from the sun with per-sample decay;
    ///   2) additive composite onto the camera colour before post-processing, so bloom/tonemapping grade the rays.
    /// All parameters come from <see cref="SunGodRaysModule.Active"/>; the pass is skipped entirely when the module
    /// is missing, disabled, faded out by elevation, or the sun is behind the camera — zero cost when invisible.
    /// </summary>
    public class SunGodRaysFeature : ScriptableRendererFeature
    {
        const string ShaderName = "Hidden/IslandSystem/SunGodRays";

        Material _material;
        GodRaysPass _pass;

        public override void Create()
        {
            _pass = new GodRaysPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null) return;
                _material = CoreUtils.CreateEngineMaterial(shader);
            }
            var module = SunGodRaysModule.Active;
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

        class GodRaysPass : ScriptableRenderPass
        {
            static readonly int ID_SunUV = Shader.PropertyToID("_GR_SunUV");
            static readonly int ID_Mask = Shader.PropertyToID("_GR_Mask");
            static readonly int ID_Blur = Shader.PropertyToID("_GR_Blur");
            static readonly int ID_Noise = Shader.PropertyToID("_GR_Noise");
            static readonly int ID_Streak = Shader.PropertyToID("_GR_Streak");
            static readonly int ID_Composite = Shader.PropertyToID("_GR_Composite");
            static readonly int ID_Color = Shader.PropertyToID("_GR_Color");
            static readonly int ID_Texel = Shader.PropertyToID("_GR_Texel");

            Material _material;
            public void Setup(Material material) => _material = material;

            class PassData
            {
                public Material material;
                public TextureHandle source;
                public int shaderPass;
                public bool setParams;
                public Vector4 sunUV, mask, blur, noise, streak, composite, texel;
                public Color color;
            }

            static void Execute(PassData d, RasterGraphContext ctx)
            {
                if (d.setParams)
                {
                    ctx.cmd.SetGlobalVector(ID_SunUV, d.sunUV);
                    ctx.cmd.SetGlobalVector(ID_Mask, d.mask);
                    ctx.cmd.SetGlobalVector(ID_Blur, d.blur);
                    ctx.cmd.SetGlobalVector(ID_Noise, d.noise);
                    ctx.cmd.SetGlobalVector(ID_Streak, d.streak);
                    ctx.cmd.SetGlobalVector(ID_Composite, d.composite);
                    ctx.cmd.SetGlobalVector(ID_Texel, d.texel);
                    ctx.cmd.SetGlobalColor(ID_Color, d.color);
                }
                Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, d.shaderPass);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var module = SunGodRaysModule.Active;
                if (module == null || !module.isActiveAndEnabled || module.FinalIntensity <= 0.0001f || _material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView) return;
                if (!resourceData.cameraDepthTexture.IsValid()) return;

                var cam = cameraData.camera;
                Vector3 toSun = module.ToSunWorld;
                float facing = Vector3.Dot(cam.transform.forward, toSun);
                if (facing <= 0.02f) return;

                Vector3 vp = cam.WorldToViewportPoint(cam.transform.position + toSun * 10000f);
                float edge = Mathf.Min(Mathf.Min(vp.x, 1f - vp.x), Mathf.Min(vp.y, 1f - vp.y));   // negative when off screen
                float fade = Mathf.Pow(Mathf.Clamp01(1f + edge * 1.5f), module.screenEdgeFade) * Mathf.Clamp01(facing * 4f);
                if (fade <= 0.002f) return;

                var desc = cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.colorFormat = RenderTextureFormat.ARGBHalf;
                int ds = 1 << (int)module.downsample;
                desc.width = Mathf.Max(8, desc.width / ds);
                desc.height = Mathf.Max(8, desc.height / ds);

                TextureHandle texA = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_SunGodRaysA", false, FilterMode.Bilinear);
                TextureHandle texB = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_SunGodRaysB", false, FilterMode.Bilinear);

                float time = Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartup % 3600.0);

                // ---- Pass 0: occlusion mask (depth as blit source) ----
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sun God Rays - Mask", out var d))
                {
                    d.material = _material;
                    d.shaderPass = 0;
                    d.source = resourceData.cameraDepthTexture;
                    d.setParams = true;
                    d.sunUV = new Vector4(vp.x, vp.y, cam.aspect, fade);
                    d.mask = new Vector4(module.maxDistance, module.sharpness, module.edgeSoftness, 0f);
                    d.blur = new Vector4(module.rayLength / Mathf.Max(1, module.blurIterations), module.falloffExponent, module.scattering, module.sampleCount);
                    d.noise = new Vector4(module.noiseScale, module.noiseStrength, time * module.animationSpeed, 0f);
                    d.streak = new Vector4(Mathf.Round(module.streakCount), module.streakStrength, module.streakSharpness, 0f);
                    d.composite = new Vector4(module.brightness, module.FinalIntensity, module.opacity, 0f);
                    d.color = module.RayColor;
                    d.texel = new Vector4(1f / desc.width, 1f / desc.height, desc.width, desc.height);
                    builder.UseTexture(d.source);
                    builder.SetRenderAttachment(texA, 0);
                    builder.AllowGlobalStateModification(true);   // this pass uploads the _GR_* globals
                    builder.SetRenderFunc<PassData>(Execute);
                }

                // ---- Pass 1: radial blur ping-pong ----
                TextureHandle src = texA, dst = texB;
                int iterations = Mathf.Clamp(module.blurIterations, 1, 3);
                for (int i = 0; i < iterations; i++)
                {
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sun God Rays - Radial", out var d))
                    {
                        d.material = _material;
                        d.shaderPass = 1;
                        d.source = src;
                        d.setParams = false;
                        builder.UseTexture(d.source);
                        builder.SetRenderAttachment(dst, 0);
                        builder.SetRenderFunc<PassData>(Execute);
                    }
                    (src, dst) = (dst, src);
                }

                // ---- Pass 2: additive composite onto camera colour ----
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sun God Rays - Composite", out var d))
                {
                    d.material = _material;
                    d.shaderPass = 2;
                    d.source = src;
                    d.setParams = false;
                    builder.UseTexture(d.source);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    builder.SetRenderFunc<PassData>(Execute);
                }
            }
        }
    }
}
