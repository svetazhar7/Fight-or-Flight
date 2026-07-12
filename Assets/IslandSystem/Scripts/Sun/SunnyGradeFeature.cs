using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace IslandSystem.Sun
{
    /// <summary>
    /// URP RenderGraph feature for <see cref="SunnyColorGrading"/> — the hue-selective warm grade + custom warm
    /// bloom. Runs BeforeRenderingPostProcessing (in HDR, before the URP uber pass), so the volume's tonemapping
    /// and global adjustments grade its output:
    ///   0) bloom prefilter (quadratic knee, warm colour isolation, golden per-source tint) at half res;
    ///   1) N disc-blur ping-pongs with growing radius (scatter);
    ///   2) selective grade + bloom composite into a full-res temp;
    ///   3) copy back to the camera colour.
    /// All parameters come from <see cref="SunnyColorGrading.Active"/>; zero cost when the component is missing,
    /// disabled, or effectStrength is 0.
    /// </summary>
    public class SunnyGradeFeature : ScriptableRendererFeature
    {
        const string ShaderName = "Hidden/IslandSystem/SunnyGrade";

        Material _material;
        GradePass _pass;

        public override void Create()
        {
            _pass = new GradePass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null) return;
                _material = CoreUtils.CreateEngineMaterial(shader);
            }
            var grading = SunnyColorGrading.Active;
            if (grading == null || !grading.isActiveAndEnabled || grading.effectStrength <= 0.001f) return;

            _pass.Setup(_material);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        class GradePass : ScriptableRenderPass
        {
            static readonly int ID_BloomA = Shader.PropertyToID("_SG_BloomA");
            static readonly int ID_BloomB = Shader.PropertyToID("_SG_BloomB");
            static readonly int ID_BloomTint = Shader.PropertyToID("_SG_BloomTint");
            static readonly int ID_GradeA = Shader.PropertyToID("_SG_GradeA");
            static readonly int ID_GradeB = Shader.PropertyToID("_SG_GradeB");
            static readonly int ID_GradeC = Shader.PropertyToID("_SG_GradeC");
            static readonly int ID_WarmColor = Shader.PropertyToID("_SG_WarmColor");
            static readonly int ID_CoolColor = Shader.PropertyToID("_SG_CoolColor");
            static readonly int ID_LiftColor = Shader.PropertyToID("_SG_LiftColor");
            static readonly int ID_Texel = Shader.PropertyToID("_SG_Texel");
            static readonly int ID_BloomTex = Shader.PropertyToID("_SG_BloomTex");

            Material _material;
            public void Setup(Material material) => _material = material;

            class PassData
            {
                public Material material;
                public TextureHandle source;
                public TextureHandle bloom;      // grade pass only
                public int shaderPass;
                public bool setParams, setBlur, bindBloom;
                public Vector4 bloomA, bloomB, gradeA, gradeB, gradeC, texel;
                public Color bloomTint, warmColor, coolColor, liftColor;
            }

            static void Execute(PassData d, RasterGraphContext ctx)
            {
                if (d.setParams)
                {
                    ctx.cmd.SetGlobalVector(ID_BloomA, d.bloomA);
                    ctx.cmd.SetGlobalVector(ID_GradeA, d.gradeA);
                    ctx.cmd.SetGlobalVector(ID_GradeB, d.gradeB);
                    ctx.cmd.SetGlobalVector(ID_GradeC, d.gradeC);
                    ctx.cmd.SetGlobalVector(ID_Texel, d.texel);
                    ctx.cmd.SetGlobalColor(ID_BloomTint, d.bloomTint);
                    ctx.cmd.SetGlobalColor(ID_WarmColor, d.warmColor);
                    ctx.cmd.SetGlobalColor(ID_CoolColor, d.coolColor);
                    ctx.cmd.SetGlobalColor(ID_LiftColor, d.liftColor);
                }
                if (d.setBlur) ctx.cmd.SetGlobalVector(ID_BloomB, d.bloomB);
                if (d.bindBloom) ctx.cmd.SetGlobalTexture(ID_BloomTex, d.bloom);
                Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, d.shaderPass);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var g = SunnyColorGrading.Active;
                if (g == null || !g.isActiveAndEnabled || g.effectStrength <= 0.001f || _material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView) return;

                var fullDesc = cameraData.cameraTargetDescriptor;
                fullDesc.depthBufferBits = 0;
                fullDesc.msaaSamples = 1;

                var halfDesc = fullDesc;
                halfDesc.colorFormat = RenderTextureFormat.ARGBHalf;
                halfDesc.width = Mathf.Max(8, fullDesc.width / 2);
                halfDesc.height = Mathf.Max(8, fullDesc.height / 2);

                bool bloomOn = g.bloomIntensity > 0.001f;

                var gradeA = new Vector4(g.exposure, g.highlightWarmth * g.warmHighlightStrength, g.shadowCoolness, g.warmSaturation);
                var gradeB = new Vector4(g.bluePreservation, g.skyColorProtection, g.coldColorPreservation, g.shadowLift);
                var gradeC = new Vector4(g.localContrast, g.highlightCompression, g.effectStrength, 0f);
                var bloomA = new Vector4(g.bloomThreshold, g.bloomWarmth, g.bloomColorIsolation, bloomOn ? g.bloomIntensity : 0f);
                var fullTexel = new Vector4(1f / fullDesc.width, 1f / fullDesc.height, fullDesc.width, fullDesc.height);
                var halfTexel = new Vector2(1f / halfDesc.width, 1f / halfDesc.height);

                TextureHandle bloomFinal = renderGraph.defaultResources.blackTexture;

                if (bloomOn)
                {
                    TextureHandle texA = UniversalRenderer.CreateRenderGraphTexture(renderGraph, halfDesc, "_SunnyBloomA", false, FilterMode.Bilinear);
                    TextureHandle texB = UniversalRenderer.CreateRenderGraphTexture(renderGraph, halfDesc, "_SunnyBloomB", false, FilterMode.Bilinear);

                    // ---- Pass 0: prefilter (camera colour → half res bloom seed) ----
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sunny Grade - Prefilter", out var d))
                    {
                        d.material = _material;
                        d.shaderPass = 0;
                        d.source = resourceData.activeColorTexture;
                        // PassData instances are POOLED by the render graph — set every flag in every pass or a
                        // stale true leaks into a pass without AllowGlobalStateModification (hard black screen).
                        d.setParams = true; d.setBlur = false; d.bindBloom = false;
                        d.bloomA = bloomA; d.gradeA = gradeA; d.gradeB = gradeB; d.gradeC = gradeC; d.texel = fullTexel;
                        d.bloomTint = g.bloomTint; d.warmColor = g.warmColor; d.coolColor = g.coolColor; d.liftColor = g.shadowLiftColor;
                        builder.UseTexture(d.source);
                        builder.SetRenderAttachment(texA, 0);
                        builder.AllowGlobalStateModification(true);
                        builder.SetRenderFunc<PassData>(Execute);
                    }

                    // ---- Pass 1..N: disc blur ping-pong, radius grows with scatter ----
                    int iterations = 2 + Mathf.RoundToInt(g.bloomScatter * 2f);   // 2..4
                    TextureHandle src = texA, dst = texB;
                    for (int i = 0; i < iterations; i++)
                    {
                        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sunny Grade - Blur", out var d))
                        {
                            d.material = _material;
                            d.shaderPass = 1;
                            d.source = src;
                            d.setParams = false; d.setBlur = true; d.bindBloom = false;
                            float radius = g.bloomRadius * (1f + i * (0.6f + g.bloomScatter));
                            d.bloomB = new Vector4(radius, 0f, halfTexel.x, halfTexel.y);
                            builder.UseTexture(d.source);
                            builder.SetRenderAttachment(dst, 0);
                            builder.AllowGlobalStateModification(true);
                            builder.SetRenderFunc<PassData>(Execute);
                        }
                        (src, dst) = (dst, src);
                    }
                    bloomFinal = src;
                }

                // ---- Pass 2: selective grade + bloom composite → full-res temp ----
                TextureHandle graded = UniversalRenderer.CreateRenderGraphTexture(renderGraph, fullDesc, "_SunnyGraded", false, FilterMode.Bilinear);
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sunny Grade - Grade", out var d))
                {
                    d.material = _material;
                    d.shaderPass = 2;
                    d.source = resourceData.activeColorTexture;
                    d.bloom = bloomFinal;
                    d.bindBloom = true;
                    d.setBlur = false;
                    d.setParams = !bloomOn;   // params were uploaded by the prefilter when bloom ran
                    d.bloomA = bloomA; d.gradeA = gradeA; d.gradeB = gradeB; d.gradeC = gradeC; d.texel = fullTexel;
                    d.bloomTint = g.bloomTint; d.warmColor = g.warmColor; d.coolColor = g.coolColor; d.liftColor = g.shadowLiftColor;
                    builder.UseTexture(d.source);
                    builder.UseTexture(d.bloom);
                    builder.SetRenderAttachment(graded, 0);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc<PassData>(Execute);
                }

                // ---- Pass 3: copy back to the camera colour ----
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sunny Grade - Copy", out var d))
                {
                    d.material = _material;
                    d.shaderPass = 3;
                    d.source = graded;
                    d.setParams = false; d.setBlur = false; d.bindBloom = false;
                    builder.UseTexture(d.source);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    builder.SetRenderFunc<PassData>(Execute);
                }
            }
        }
    }
}
