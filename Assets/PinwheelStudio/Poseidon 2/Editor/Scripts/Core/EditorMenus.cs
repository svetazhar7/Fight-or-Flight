#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace Pinwheel.Poseidon
{
    public static class EditorMenus
    {
        [MenuItem("GameObject/3D Object/Water (Poseidon)/Tileable Water")]
        public static TileableWater CreateTileableWater(MenuCommand cmd)
        {
            GameObject g = new GameObject("Tileable Water");
            if (cmd != null && cmd.context != null)
            {
                GameObject root = cmd.context as GameObject;
                GameObjectUtility.SetParentAndAlign(g, root);
            }

            TileableWater waterComponent = g.AddComponent<TileableWater>();
            Selection.activeGameObject = g;

            return waterComponent;
        }

        [MenuItem("GameObject/3D Object/Water (Poseidon)/Area Water")]
        public static AreaWater CreateAreaWater(MenuCommand cmd)
        {
            GameObject g = new GameObject("Area Water");
            if (cmd != null && cmd.context != null)
            {
                GameObject root = cmd.context as GameObject;
                GameObjectUtility.SetParentAndAlign(g, root);
            }

            AreaWater waterComponent = g.AddComponent<AreaWater>();
            Selection.activeGameObject = g;

            return waterComponent;
        }

        [MenuItem("GameObject/3D Object/Water (Poseidon)/River")]
        public static RiverWater CreateRiver(MenuCommand cmd)
        {
            GameObject g = new GameObject("River");
            if (cmd != null && cmd.context != null)
            {
                GameObject root = cmd.context as GameObject;
                GameObjectUtility.SetParentAndAlign(g, root);
            }

            RiverWater waterComponent = g.AddComponent<RiverWater>();
            Selection.activeGameObject = g;

            return waterComponent;
        }

        [MenuItem("Window/Poseidon/Version Info")]
        public static void ShowVersionInfo()
        {
            EditorUtility.DisplayDialog(
                "Version Info",
                PVersionInfo.ProductNameAndVersion,
                "OK");
        }

        [MenuItem("Window/Poseidon/Online Manual")]
        public static void ShowOnlineUserGuide()
        {
            Application.OpenURL(PCommon.ONLINE_MANUAL);
        }

        [MenuItem("Window/Poseidon/Contact")]
        public static void ShowContactPage()
        {
            Application.OpenURL(PCommon.CONTACT_PAGE);
        }

        //[MenuItem("Poseidon Internal/Set Scene View size for screenshot")]
        //public static void SetSceneViewSizeForScreenShot()
        //{
        //    SceneView sv = SceneView.lastActiveSceneView;
        //    if (sv == null)
        //        return;

        //    Vector2 pos = new Vector2(100, 100);
        //    Vector2 size = new Vector2(2048, 1152-23);
        //    sv.position = EditorGUIUtility.PixelsToPoints(new Rect(pos, size));
        //}

        //[MenuItem("Poseidon Internal/Set focused window size for screenshot")]
        //public static void SetGameViewSizeForScreenShot()
        //{
        //    EditorWindow w = EditorWindow.focusedWindow;
        //    if (w == null)
        //        return;

        //    Vector2 pos = new Vector2(100, 100);
        //    Vector2 size = new Vector2(2048, 1152 - 23);
        //    w.position = EditorGUIUtility.PixelsToPoints(new Rect(pos, size));
        //}
    }
}

#endif