using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Distance culling for a holder of spawned GameObjects (rocks, props, village buildings). Unity already
    /// FRUSTUM-culls renderers automatically, so the missing optimization for these objects is hiding the ones
    /// that are far away but still in view — they otherwise render full triangles from across the island / from
    /// the air. This toggles each child's renderers by distance to the viewer (keeping the GameObjects and their
    /// COLLIDERS intact, so buildings/rocks stay solid). Work is spread across frames so a big holder never
    /// hitches. Put it on the holder; it (re)collects children when their count changes.
    /// </summary>
    [ExecuteAlways]
    public class SpawnCuller : MonoBehaviour
    {
        [Tooltip("Children farther than this from the viewer stop rendering (colliders stay). Small ground " +
                 "detail (rocks/props) wants ~200 m; landmark buildings want a larger radius.")]
        public float cullDistance = 220f;

        Transform[] _items;
        Renderer[][] _rends;
        bool[] _vis;
        int _lastCount = -1;
        int _cursor;

        void Collect()
        {
            int n = transform.childCount;
            _items = new Transform[n];
            _rends = new Renderer[n][];
            _vis = new bool[n];
            for (int i = 0; i < n; i++)
            {
                _items[i] = transform.GetChild(i);
                _rends[i] = _items[i].GetComponentsInChildren<Renderer>(true);
                _vis[i] = true;
            }
            _lastCount = n;
            _cursor = 0;
        }

        void LateUpdate()
        {
            if (transform.childCount != _lastCount) Collect();
            int count = _items != null ? _items.Length : 0;
            if (count == 0) return;

            Vector3 p = ViewerPos();
            float maxSq = cullDistance * cullDistance;

            // Process a slice each frame (round-robin) so the toggle cost is amortised — visibility of any one
            // object updates within a few frames, which is imperceptible at these distances.
            int budget = Mathf.Clamp(count / 6, 24, count);
            for (int k = 0; k < budget; k++)
            {
                _cursor = (_cursor + 1) % count;
                var t = _items[_cursor];
                if (t == null) continue;
                bool vis = (t.position - p).sqrMagnitude < maxSq;
                if (vis == _vis[_cursor]) continue;
                _vis[_cursor] = vis;
                var rs = _rends[_cursor];
                for (int r = 0; r < rs.Length; r++) if (rs[r]) rs[r].enabled = vis;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        Vector3 ViewerPos()
        {
            if (Application.isPlaying && IslandGrassField.LocalViewer != null) return IslandGrassField.LocalViewer.position;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null) return sv.camera.transform.position;
            }
#endif
            var c = Camera.main;
            return c != null ? c.transform.position : transform.position;
        }
    }
}
