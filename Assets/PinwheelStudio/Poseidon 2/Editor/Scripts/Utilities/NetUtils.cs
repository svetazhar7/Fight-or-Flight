#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.Text;
using Unity.EditorCoroutines.Editor;

namespace Pinwheel.Poseidon
{
    public enum UILocation
    {
        Inspector
    }

    public static class NetUtils
    {
        public static void TrackClick(string button_name, UILocation location)
        {
            const string ENDPOINT_URL = "https://api.pinwheelstud.io/pwi/editor/btn-click/";

            string buttonId = $"{button_name.ToLower().Replace(" ", "_")}__{location.ToString().ToLower()}";
            if (string.IsNullOrEmpty(buttonId))
                return;

            var payload =
                "{\"product\":\"" + Escape(PVersionInfo.ProductNameShort) +
                "\",\"button_id\":\"" + Escape(buttonId) + "\"}";

            var request = new UnityWebRequest(ENDPOINT_URL, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.disposeUploadHandlerOnDispose = true;
            request.disposeDownloadHandlerOnDispose = true;

            request.SetRequestHeader("Content-Type", "application/json");
            var ops = request.SendWebRequest();
            ops.completed += _ => { request.Dispose(); };
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public delegate void ApiCallback<ResponseType>(UnityWebRequest request, ResponseType response) where ResponseType : class, new();
        public static IEnumerator SendRequest<T>(UnityWebRequest request, ApiCallback<T> callback) where T : class, new()
        {
            yield return request.SendWebRequest();

            T response = new T();
            if (request.result == UnityWebRequest.Result.Success)
            {
                JsonUtility.FromJsonOverwrite(request.downloadHandler.text, response);
            }
            callback.Invoke(request, response);
        }
    }
}

#endif