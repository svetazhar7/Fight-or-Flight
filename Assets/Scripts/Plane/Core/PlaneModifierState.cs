using UnityEngine;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Aggregated, per-tick multipliers/offsets that any number of effects
    /// (damage, icing, overheat, cargo shift, storm...) contribute into. The
    /// flight model and engine read these — so a new mechanic is a new
    /// <see cref="IPlaneCondition"/> that writes here, with ZERO changes to the
    /// physics code. Rebuilt from scratch every tick (see PlaneController).
    /// </summary>
    public sealed class PlaneModifierState
    {
        public float LiftMultiplier;
        public float DragMultiplier;
        public float ThrustMultiplier;
        public float ControlMultiplier;

        /// <summary>Added to the airframe's base centre of mass (cargo shift).</summary>
        public Vector3 CenterOfMassOffset;

        /// <summary>Added to base mass (cargo loaded/dropped). Apply where you set rb.mass.</summary>
        public float MassDelta;

        public void Reset()
        {
            LiftMultiplier = 1f;
            DragMultiplier = 1f;
            ThrustMultiplier = 1f;
            ControlMultiplier = 1f;
            CenterOfMassOffset = Vector3.zero;
            MassDelta = 0f;
        }
    }
}
