#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using System.Linq;
using System;

namespace Pinwheel.Poseidon
{
    public class PoseidonDebugWindow : EditorWindow
    {


        //[MenuItem("Window/Poseidon/Debug")]
        public static void ShowWindow()
        {
            PoseidonDebugWindow window = GetWindow<PoseidonDebugWindow>();
            window.titleContent = new GUIContent("PoseidonDebug");
            window.Show();
        }

        public void OnEnable()
        {

        }

        public void OnDisable()
        {

        }

        public void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);


            EditorGUILayout.EndVertical();
        }
    }
}

#endif