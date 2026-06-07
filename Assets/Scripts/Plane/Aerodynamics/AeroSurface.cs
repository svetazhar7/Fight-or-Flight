using UnityEngine;
using FishNet.Object.Prediction;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// One lifting surface (a wing). Computes its own lift &amp; drag from the local
    /// airflow using Cl/Cd curves, and applies the force AT ITS OWN POSITION so the
    /// resulting torque is physically correct. Direction basis comes from the shared
    /// aircraft axes (scale/pivot-proof); only the application POINT is per-wing — so
    /// wing damage/loss produces emergent asymmetry and spins for free.
    /// </summary>
    public class AeroSurface : MonoBehaviour
    {
        [Tooltip("Reference wing area. Bigger = more lift and drag.")]
        [SerializeField] private float _area = 6f;

        [Tooltip("X: angle of attack (deg). Y: lift coefficient Cl. The drop after the peak IS the stall.")]
        [SerializeField] private AnimationCurve _liftCurve = DefaultLiftCurve();

        [Tooltip("X: angle of attack (deg). Y: drag coefficient Cd. U-shaped; min near 0°.")]
        [SerializeField] private AnimationCurve _dragCurve = DefaultDragCurve();

        [Tooltip("0..1 scalar on lift. Damage and icing drive this down; 0 = dead wing.")]
        [Range(0f, 1f)][SerializeField] private float _effectiveness = 1f;

        public float Effectiveness
        {
            get => _effectiveness;
            set => _effectiveness = Mathf.Clamp01(value);
        }

        public bool IsAttached { get; set; } = true;
        public float LastAoA { get; private set; }

        public void ApplyForces(PredictionRigidbody body, float airDensity, PlaneModifierState mod, in PlaneAxes axes)
        {
            if (!IsAttached || _effectiveness <= 0f)
                return;

            Rigidbody rb = body.Rigidbody;
            Vector3 worldVel = rb.GetPointVelocity(transform.position); // includes airframe rotation
            float speedSqr = worldVel.sqrMagnitude;
            if (speedSqr < 0.04f)
                return;

            // Angle of attack from airflow projected onto the aircraft forward/up axes.
            float vForward = Vector3.Dot(worldVel, axes.Forward);
            float vUp = Vector3.Dot(worldVel, axes.Up);
            float aoaDeg = Mathf.Atan2(-vUp, Mathf.Max(0.001f, vForward)) * Mathf.Rad2Deg;
            LastAoA = aoaDeg;

            float q = 0.5f * airDensity * speedSqr; // dynamic pressure
            float cl = _liftCurve.Evaluate(aoaDeg) * _effectiveness * mod.LiftMultiplier;
            float cd = _dragCurve.Evaluate(aoaDeg) * mod.DragMultiplier;

            Vector3 dragDir = -worldVel / Mathf.Sqrt(speedSqr);
            Vector3 liftDir = Vector3.Cross(axes.Right, dragDir).normalized; // perpendicular to flow, toward +up

            Vector3 force = liftDir * (q * _area * cl) + dragDir * (q * _area * cd);
            body.AddForceAtPosition(force, transform.position);
        }

        private static AnimationCurve DefaultLiftCurve()
        {
            return new AnimationCurve(
                new Keyframe(-30f, -0.5f),
                new Keyframe(-15f, -1.1f),
                new Keyframe(0f, 0f),
                new Keyframe(15f, 1.2f),  // peak ~ critical AoA
                new Keyframe(20f, 0.9f),  // stall: Cl collapses
                new Keyframe(30f, 0.5f),
                new Keyframe(50f, 0.25f));
        }

        private static AnimationCurve DefaultDragCurve()
        {
            return new AnimationCurve(
                new Keyframe(-50f, 0.6f),
                new Keyframe(-20f, 0.12f),
                new Keyframe(0f, 0.025f),  // minimum drag → glide ratio
                new Keyframe(20f, 0.12f),
                new Keyframe(50f, 0.6f));
        }
    }
}
