#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using UnityEditor;
using UnityEngine.Networking;
using Unity.EditorCoroutines.Editor;
using UnityEditor.Callbacks;

namespace Pinwheel.Poseidon
{
    [System.Serializable]
    public class VersionNumbers
    {
        public int major;
        public int minor;
        public int patch;
    }

    [CreateAssetMenu(menuName = "Poseidon/Version Checker")]
    [ExecuteInEditMode]
    [InitializeOnLoad]
    public class VersionChecker : ScriptableObject
    {
        public VersionNumbers newestVersion;

        private static readonly string PREF_PREFIX = "poseidon-check-update-";

        [InitializeOnLoadMethod]
        private static void Init()
        {
            Resources.Load<VersionChecker>("Poseidon/VersionChecker");
        }

        internal static bool CheckedToday()
        {
            string dateString = DateTime.Now.ToString("yyyy-MM-dd");
            return EditorPrefs.HasKey(PREF_PREFIX + dateString);
        }

        internal static void CheckForUpdate()
        {
            string dateString = DateTime.Now.ToString("yyyy-MM-dd");
            EditorPrefs.SetBool(PREF_PREFIX + dateString, true);
            GetVersionInfo((request, response) =>
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    if (response.major > PVersionInfo.Major ||
                    response.minor > PVersionInfo.Minor ||
                    response.patch > PVersionInfo.Patch)
                    {
                        Debug.Log($"POSEIDON: New version {response.major}.{response.minor}.{response.patch} is available, please update the asset.");
                    }
                }
            });
        }

        private static UnityWebRequest GetVersionInfo(NetUtils.ApiCallback<VersionNumbers> callback)
        {
            string url = $"https://api.pinwheelstud.io/poseidon/2/version-info";
            UnityWebRequest request = UnityWebRequest.Get(url);
            EditorCoroutineUtility.StartCoroutineOwnerless(NetUtils.SendRequest(request, callback));
            return request;
        }

        private void OnEnable()
        {
            if (!CheckedToday())
            {
                CheckForUpdate();
            }
        }

        private void OnDisable()
        {

        }
    }
}

#endif