using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Drives Poseidon 2 water wave animation on a plain water plane.
    ///
    /// Poseidon animates its waves from the material floats <c>_PoseidonTime</c> / <c>_PoseidonSineTime</c>,
    /// which are normally fed each frame by a <c>PoseidonWaterBody</c> (e.g. <c>AreaWater.Update</c>). Our
    /// ocean is a plain flat <see cref="PrimitiveType.Plane"/> with the Poseidon material assigned directly
    /// (no AreaWater — its area rasterizer crashes on rectangular outlines, which is why we don't use it and
    /// keep the ocean a square). With no water body, nothing sets those floats, so the water sits still.
    /// This component feeds them itself, exactly like AreaWater does (Auto time = realtime seconds).
    ///
    /// Uses a MaterialPropertyBlock so the shared water material asset isn't modified/dirtied.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class PoseidonWaterAnimator : MonoBehaviour
    {
        static readonly int TimeId = Shader.PropertyToID("_PoseidonTime");
        static readonly int SineTimeId = Shader.PropertyToID("_PoseidonSineTime");

        Renderer _rend;
        MaterialPropertyBlock _mpb;

        void OnEnable()
        {
            _rend = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (_rend == null) _rend = GetComponent<Renderer>();
            if (_rend == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            float t = Time.realtimeSinceStartup;
            _rend.GetPropertyBlock(_mpb);
            _mpb.SetFloat(TimeId, t);
            _mpb.SetFloat(SineTimeId, Mathf.Sin(t));
            _rend.SetPropertyBlock(_mpb);

#if UNITY_EDITOR
            // Keep the water animating in the editor too (not just Play), like Poseidon's own water bodies.
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }
    }
}
