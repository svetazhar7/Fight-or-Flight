using System.Collections.Generic;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Feeds the positions of nearby interactors (players) to the <c>IslandSystem/Grass</c> shader each frame so
    /// the grass flattens under them. Auto-creates itself when the first <see cref="GrassInteractor"/> appears;
    /// works for several players at once (multiplayer) up to <see cref="MaxInteractors"/>.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class GrassInteractionManager : MonoBehaviour
    {
        public const int MaxInteractors = 16;

        static GrassInteractionManager _instance;
        static readonly List<GrassInteractor> _interactors = new List<GrassInteractor>();
        static readonly Vector4[] _data = new Vector4[MaxInteractors];
        static readonly int IdArray = Shader.PropertyToID("_GrassInteractors");
        static readonly int IdCount = Shader.PropertyToID("_GrassInteractorCount");

        public static void Register(GrassInteractor i)
        {
            if (i != null && !_interactors.Contains(i)) _interactors.Add(i);
            EnsureInstance();
        }

        public static void Unregister(GrassInteractor i) => _interactors.Remove(i);

        static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("GrassInteractionManager") { hideFlags = HideFlags.DontSave };
            _instance = go.AddComponent<GrassInteractionManager>();
            if (Application.isPlaying) DontDestroyOnLoad(go);
        }

        void LateUpdate()
        {
            int n = 0;
            for (int i = 0; i < _interactors.Count && n < MaxInteractors; i++)
            {
                var it = _interactors[i];
                if (it == null || !it.isActiveAndEnabled) continue;
                Vector3 p = it.transform.position;
                _data[n] = new Vector4(p.x, p.y, p.z, Mathf.Max(0f, it.radius));
                n++;
            }
            for (int i = n; i < MaxInteractors; i++) _data[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(IdArray, _data);
            Shader.SetGlobalInt(IdCount, n);
        }
    }

    // GrassInteractor lives in its own file (GrassInteractor.cs) — Unity only serializes a MonoBehaviour
    // on prefabs/scenes when the class name matches its file name.
}
