using UnityEngine;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Orthonormal aircraft basis in WORLD space, derived from the Rigidbody's
    /// rotation (NOT transform.forward). This is correct even when the airframe has
    /// non-uniform scale or a rotated model pivot — both of which skew transform axes.
    /// </summary>
    public readonly struct PlaneAxes
    {
        public readonly Vector3 Forward; // nose
        public readonly Vector3 Up;      // lift
        public readonly Vector3 Right;   // starboard

        public PlaneAxes(Vector3 forward, Vector3 up, Vector3 right)
        {
            Forward = forward;
            Up = up;
            Right = right;
        }

        /// <param name="localForward">Airframe-local axis pointing out the nose.</param>
        /// <param name="localUp">Airframe-local axis pointing "up" (lift direction).</param>
        public static PlaneAxes From(Rigidbody rb, Vector3 localForward, Vector3 localUp)
        {
            Quaternion q = rb.rotation;
            Vector3 f = (q * localForward).normalized;
            Vector3 r = Vector3.Cross((q * localUp).normalized, f).normalized; // right = up × forward
            Vector3 u = Vector3.Cross(f, r).normalized;                        // re-orthogonalized up
            return new PlaneAxes(f, u, r);
        }
    }
}
