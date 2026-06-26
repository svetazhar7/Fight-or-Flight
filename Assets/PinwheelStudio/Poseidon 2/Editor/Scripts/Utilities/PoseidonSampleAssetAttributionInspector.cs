#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace Pinwheel.Poseidon
{
    [CustomEditor(typeof(PoseidonSampleAssetAttribution))]
    public class PoseidonSampleAssetAttributionInspector : Editor
    {
        private PoseidonSampleAssetAttribution instance;
        private void OnEnable()
        {
            instance = target as PoseidonSampleAssetAttribution;
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField(instance.text, PEditorCommon.WordWrapItalicLabel);
            if (!string.IsNullOrEmpty(instance.linkText) && !string.IsNullOrEmpty(instance.linkUrl))
            {
                if (EditorGUILayout.LinkButton(instance.linkText + " →"))
                {
                    NetUtils.TrackClick("learn_more_polaris", UILocation.Inspector);
                    Application.OpenURL(instance.linkUrl);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("This component has no functionality at runtime", EditorStyles.miniLabel);
        }
    }
}

#endif