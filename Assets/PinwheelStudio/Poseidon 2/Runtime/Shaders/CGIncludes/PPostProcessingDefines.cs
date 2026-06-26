#if POSEIDON_2
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

namespace Pinwheel.Poseidon
{
    [InitializeOnLoad]
    public static class PPostProcessingDefines
    {
        [InitializeOnLoadMethod]
        private static void OnInit()
        {
            GeneratePostProcessingDefines();
        }

        private static void GeneratePostProcessingDefines()
        {
            List<string> guids = new List<string>(AssetDatabase.FindAssets("PPostProcessingDefines"));
            if (guids.Count > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                string folder = Path.GetDirectoryName(path);
                string generatedFilePath = Path.Combine(folder, "PPostProcessingDefines.cs.cginc");
                string uvProp;
#if UNITY_2022_2_OR_NEWER
                uvProp = "texcoord";
#else
                uvProp = "uv";
#endif

                bool isBiRP = PCommon.CurrentRenderPipeline == RenderPipelineType.Builtin;
                bool isURP = PCommon.CurrentRenderPipeline == RenderPipelineType.Universal;
                bool isPPv2Installed =
#if UNITY_POST_PROCESSING_STACK_V2
                    true;
#else
                    false;
#endif

                string content =
                    "//This file was generated, please don't edit by hand\n" +
                    "#ifndef PPOST_PROCESSING_DEFINES_CS_INCLUDED\n" +
                    "#define PPOST_PROCESSING_DEFINES_CS_INCLUDED\n" +
                    "\n" +
                    $"#{(isPPv2Installed?"define":"undef")} POSEIDON_PPV2_INSTALLED\n" +
                    $"#{(isURP ? "define" : "undef")} POSEIDON_RENDER_PIPELINE_UNIVERSAL\n" +
                    $"#define UV(i) i.{uvProp}\n" +
                    "\n" +
                    "#endif";
                File.WriteAllText(generatedFilePath, content);
            }
        }
    }
}
#endif
#endif