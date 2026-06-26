#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Rendering;
using DateTime = System.DateTime;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pinwheel.Poseidon
{
    public static class PCommon
    {
        public const string SUPPORT_EMAIL = "support@pinwheel.studio";
        public const string BUSINESS_EMAIL = "hello@pinwheel.studio";
        public const string YOUTUBE_CHANNEL = "https://www.youtube.com/channel/UCebwuk5CfIe5kolBI9nuBTg";
        public const string ONLINE_MANUAL = "https://docs.pinwheelstud.io/poseidon/2/";
        public const string FACEBOOK_PAGE = "https://www.facebook.com/polaris.terrain";
        public const string FORUM = "https://forum.unity.com/threads/released-poseidon-low-poly-water-system-builtin-lwrp.746138/";
        public const string DISCORD = "https://discord.gg/6kkDvj6";
        public const string CONTACT_PAGE = "https://pinwheelstud.io/contact";
        public const string POLARIS_LINK = "https://assetstore.unity.com/packages/tools/terrain/polaris-3-low-poly-terrain-tool-286886?aid=1100l3QbW&pubref=poseidon-editor";
        public const string JUPITER_LINK = "https://assetstore.unity.com/packages/2d/textures-materials/sky/procedural-sky-shader-day-night-cycle-jupiter-159992?aid=1100l3QbW&pubref=poseidon-editor";

        public const int PREVIEW_TEXTURE_SIZE = 512;
        public const int TEXTURE_SIZE_MIN = 1;
        public const int TEXTURE_SIZE_MAX = 8192;

        public static RenderPipelineType CurrentRenderPipeline
        {
            get
            {
                RenderPipelineAsset rpAsset = GraphicsSettings.currentRenderPipeline;
                if (rpAsset == null)
                {
                    return RenderPipelineType.Builtin;
                }
                else if (rpAsset.GetType().Name.Equals("UniversalRenderPipelineAsset"))
                {
                    return RenderPipelineType.Universal;
                }
                else
                {
                    return RenderPipelineType.Unsupported;
                }
            }
        }
    }
}
#endif