using UnityEngine;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// The propeller as a damageable part. When it breaks/detaches the engine flames
    /// out (thrust → 0) and the prop stops spinning, leaving the plane to glide.
    /// </summary>
    public class PropellerPart : PlanePart
    {
        [SerializeField] private PlaneEngine _engine;
        [SerializeField] private PropellerAnimator _animator;
        [SerializeField] private MeshRenderer _renderer;

        protected override void OnAttachedChanged(bool prev, bool attached, bool asServer)
        {
            if (attached)
                return;

            if (_engine != null)
                _engine.IsRunning = false;
            if (_animator != null)
                _animator.Enabled = false;
            if (_renderer != null)
                _renderer.enabled = false;
        }
    }
}
