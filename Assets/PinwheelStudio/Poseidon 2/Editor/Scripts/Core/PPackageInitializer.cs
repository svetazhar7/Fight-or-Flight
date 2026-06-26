using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using System;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.Build;

namespace Pinwheel.Poseidon
{
    public static class PPackageInitializer
    {
        public static string POSEIDON_KW = "POSEIDON_2";
        public static string POSEIDON_URP_KW = "POSEIDON_2_URP";

        private static ListRequest listPackageRequest = null;
        private static AddRequest addPackageRequest = null;

#pragma warning disable 0414
        private static bool isUrpInstalled = false;
        private static bool isShaderGraphInstalled = false;
        private static bool isEditorCoroutinesInstalled = false;
#pragma warning restore 0414

        [DidReloadScripts]
        public static void Init()
        {
            isUrpInstalled = false;
            isShaderGraphInstalled = false;
            isEditorCoroutinesInstalled = false;    

            CheckUnityPackagesAndInit();
        }

        private static void CheckUnityPackagesAndInit()
        {
            listPackageRequest = Client.List(true);
            EditorApplication.update += OnRequestingPackageList;
        }

        private static void OnRequestingPackageList()
        {
            if (listPackageRequest == null)
                return;
            if (!listPackageRequest.IsCompleted)
                return;
            if (listPackageRequest.Error != null)
            {
                isUrpInstalled = false;
                isShaderGraphInstalled = false;
                isEditorCoroutinesInstalled = false;
            }
            else
            {
                isUrpInstalled = false;
                isShaderGraphInstalled = false;
                isEditorCoroutinesInstalled = false;
                foreach (UnityEditor.PackageManager.PackageInfo p in listPackageRequest.Result)
                {
                    if (p.name.Equals("com.unity.render-pipelines.universal"))
                    {
                        isUrpInstalled = true;
                    }
                    if (p.name.Equals("com.unity.shadergraph"))
                    {
                        isShaderGraphInstalled = true;
                    }
                    if (p.name.Equals("com.unity.editorcoroutines"))
                    {
                        isEditorCoroutinesInstalled = true;
                    }
                }
            }

            if (!isShaderGraphInstalled)
            {
                addPackageRequest = Client.Add("com.unity.shadergraph"); 
                EditorApplication.update += OnPackageAdded;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            else if (!isEditorCoroutinesInstalled)
            {
                addPackageRequest = Client.Add("com.unity.editorcoroutines");
                EditorApplication.update += OnPackageAdded;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            else
            {
                SetupKeywords();
            }
            EditorApplication.update -= OnRequestingPackageList;
        }

        private static void OnPackageAdded()
        {
            if (addPackageRequest == null)
                return;
            if (!addPackageRequest.IsCompleted)
                return;
            if (addPackageRequest.Error != null)
            {

            }
            else
            {
                Debug.Log($"POSEIDON: Dependency package installed [{addPackageRequest.Result.name}]");
            }

            listPackageRequest = Client.List(true, true);
            EditorApplication.update += OnRequestingPackageList;
            EditorApplication.update -= OnPackageAdded;
        }

        private static bool IsAllDependenciesInstalled()
        {
            return isShaderGraphInstalled && isEditorCoroutinesInstalled;
        }

        private static void SetupKeywords()
        {
            BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup buildGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);

            string symbols = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildGroup));
            List<string> symbolList = new List<string>(symbols.Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries));

            bool isDirty = false;
            isDirty = isDirty || SetKeywordActive(symbolList, POSEIDON_URP_KW, isUrpInstalled);
            isDirty = isDirty || SetKeywordActive(symbolList, POSEIDON_KW, IsAllDependenciesInstalled());

            if (isDirty)
            {
                symbols = ListElementsToString(symbolList, ";");
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildGroup), symbols);
            }
        }

        private static bool SetKeywordActive(List<string> kwList, string keyword, bool active)
        {
            bool isDirty = false;
            if (active && !kwList.Contains(keyword))
            {
                kwList.Add(keyword);
                isDirty = true;
                Debug.Log($"POSEIDON: Adding {keyword} to project Scripting Symbols.");
            }
            else if (!active && kwList.Contains(keyword))
            {
                kwList.RemoveAll(s => s.Equals(keyword));
                Debug.Log($"POSEIDON: Removing {keyword} from project Scripting Symbols.");
                isDirty = true;
            }
            return isDirty;
        }

        public static string ListElementsToString<T>(IEnumerable<T> list, string separator)
        {
            IEnumerator<T> i = list.GetEnumerator();
            System.Text.StringBuilder s = new System.Text.StringBuilder();
            while (i.MoveNext())
            {
                s.Append(i.Current).Append(separator);
            }
            return s.ToString();
        }
    }
}
