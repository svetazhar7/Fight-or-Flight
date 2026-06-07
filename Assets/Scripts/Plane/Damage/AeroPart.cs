using UnityEngine;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// A wing as a damageable part. Couples health to its <see cref="AeroSurface"/>
    /// effectiveness, so a damaged wing progressively loses lift (asymmetry → roll),
    /// and a detached wing stops contributing entirely (→ spin). No special "spin"
    /// code anywhere — it falls out of the per-surface aerodynamics.
    /// </summary>
    public class AeroPart : PlanePart
    {
        [SerializeField] private AeroSurface _surface;
        [SerializeField] private MeshRenderer _renderer;
        [SerializeField] private Collider _collider;

        protected override void OnHealthChanged(float prev, float next, bool asServer)
        {
            // Damaged-but-attached wing loses lift in proportion to remaining health.
            if (_surface != null && IsAttached)
                _surface.Effectiveness = Health01;
        }

        protected override void OnAttachedChanged(bool prev, bool attached, bool asServer)
        {
            if (attached)
                return;

            if (_surface != null)
            {
                _surface.IsAttached = false;
                _surface.Effectiveness = 0f;
            }
            if (_renderer != null)
                _renderer.enabled = false;
            if (_collider != null)
                _collider.enabled = false;
        }
    }
}
