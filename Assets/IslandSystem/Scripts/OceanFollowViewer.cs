using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Keeps the detailed Poseidon water tiles under the viewer: the tile block is ~1 km wide while the
    /// archipelago spans several km, so the whole block follows the viewer SNAPPED to the tile size — tiles
    /// always land back on the same world lattice, and the Poseidon waves (world-space noise) stay seamless.
    /// </summary>
    [ExecuteAlways]
    public class OceanFollowViewer : MonoBehaviour
    {
        [Tooltip("Snap step (m) — use the water tile size so relocated tiles land on the same world lattice.")]
        public float snap = 250f;

        void LateUpdate()
        {
            // Snap in the PARENT's local space (parent "Ocean" sits at world XZ 0, so local == world XZ). This
            // way local Y stays 0 and the block INHERITS the parent's Y — the tide (which moves the Ocean root
            // up/down) carries the following detail tiles with it instead of freezing at one Y.
            Vector3 v = ViewerPos();
            Vector3 parentPos = transform.parent != null ? transform.parent.position : Vector3.zero;
            float sx = Mathf.Round((v.x - parentPos.x) / snap) * snap;
            float sz = Mathf.Round((v.z - parentPos.z) / snap) * snap;
            var lp = transform.localPosition;
            if (!Mathf.Approximately(lp.x, sx) || !Mathf.Approximately(lp.z, sz) || lp.y != 0f)
                transform.localPosition = new Vector3(sx, 0f, sz);
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
