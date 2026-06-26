#if POSEIDON_2
#if UNITY_POST_PROCESSING_STACK_V2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering.PostProcessing;

namespace Pinwheel.Poseidon.FX.BiRP
{
    public class WetLensRendererBirp : PostProcessEffectRenderer<WetLensBirp>
    {
        public override void Render(PostProcessRenderContext context)
        {
            Shader shader = WaterFxControllerBirp.wetLensShader;

            PropertySheet sheet = context.propertySheets.Get(shader);
            Texture normalMap = settings.normalMap.value ?? Texture2D.normalTexture;
            sheet.properties.SetTexture(PMat.PP_WET_LENS_TEX, normalMap);
            sheet.properties.SetFloat(PMat.PP_WET_LENS_STRENGTH, settings.strength * settings.intensity);

            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }
}
#endif

#endif