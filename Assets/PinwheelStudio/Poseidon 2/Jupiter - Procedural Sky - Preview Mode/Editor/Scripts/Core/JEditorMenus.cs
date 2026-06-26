#if POSEIDON_2
#if !JUPITER
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace Pinwheel.Poseidon.JupiterPreview
{
    public static class JEditorMenus
    {
        [MenuItem("GameObject/3D Object/Sky (Jupiter Preview)/Day Sky")]
        public static JupiterSkyPreview CreateDaySky(MenuCommand cmd)
        {
            GameObject g = new GameObject("Day Sky - Jupiter Preview");
            if (cmd != null && cmd.context != null)
            {
                GameObject root = cmd.context as GameObject;
                GameObjectUtility.SetParentAndAlign(g, root);
            }

            JupiterSkyPreview skyComponent = g.AddComponent<JupiterSkyPreview>();
            skyComponent.ApplyDayPreset();

            return skyComponent;
        }

        [MenuItem("GameObject/3D Object/Sky (Jupiter Preview)/Night Sky")]
        public static JupiterSkyPreview CreateNighSky(MenuCommand cmd)
        {
            GameObject g = new GameObject("Night Sky - Jupiter Preview");
            if (cmd != null && cmd.context != null)
            {
                GameObject root = cmd.context as GameObject;
                GameObjectUtility.SetParentAndAlign(g, root);
            }

            JupiterSkyPreview skyComponent = g.AddComponent<JupiterSkyPreview>();
            skyComponent.ApplyNightPreset();

            return skyComponent;
        }
    }
}
#endif  
#endif