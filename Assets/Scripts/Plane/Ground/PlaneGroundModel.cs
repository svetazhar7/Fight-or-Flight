using UnityEngine;
using FishNet.Object.Prediction;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Simplified, MODEL-INDEPENDENT ground handling.
    ///
    /// Vertical support is left to the airframe collider resting on the runway — and
    /// <see cref="PlaneController"/> gives that collider a friction-less surface, so the
    /// plane slides like it's on wheels and thrust isn't eaten by belly friction. This
    /// model only adds the "wheel feel": nose-wheel steering, rolling resistance, lateral
    /// grip (so it tracks instead of drifting) and a roll-only keep-upright torque (roll
    /// only, so it never fights you rotating the nose up for take-off).
    ///
    /// The grounded probe is a single down-ray from the centre of mass that ignores the
    /// plane's own colliders, so it needs no wheel transforms and no special ground layer.
    /// Driven by <see cref="PlaneController"/> each prediction tick.
    /// </summary>
    public class PlaneGroundModel : MonoBehaviour
    {
        [Header("Grounded probe")]
        [SerializeField] private LayerMask _groundMask = ~0;

        [Tooltip("How far below the airframe belly still counts as 'on the ground' (m).")]
        [SerializeField] private float _groundCheck = 0.6f;

        [Header("Taxi")]
        [SerializeField] private float _steerTorque = 4000f;
        [SerializeField] private float _brakeForce = 12000f;
        [SerializeField] private float _rollingResistance = 600f;
        [SerializeField] private float _lateralGrip = 8000f;

        [Tooltip("Torque (roll only) that keeps the plane from tipping over on the ground.")]
        [SerializeField] private float _uprightTorque = 4000f;

        private PredictionRigidbody _body;
        private Collider _hull;
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];
        private bool _grounded;
        private float _hullExtentUp = -1f;   // half-thickness along up, cached at rest (tilt-proof)

        public void Bind(PredictionRigidbody body) => _body = body;

        /// <summary>
        /// Half-height of the airframe collider along the aircraft up axis. Measured ONCE
        /// while the plane is level (at spawn) and cached: if we re-projected the world AABB
        /// every tick, pitching the nose up would tilt the long fuselage axis into "up" and
        /// blow the value up, making the plane think it's grounded in mid-air.
        /// </summary>
        private float HullExtentUp(in PlaneAxes axes)
        {
            if (_hullExtentUp >= 0f)
                return _hullExtentUp;

            if (_hull == null && _body != null)
                _hull = _body.Rigidbody.GetComponent<Collider>();
            if (_hull == null)
                return 0.25f;

            Vector3 e = _hull.bounds.extents;
            _hullExtentUp = Mathf.Abs(e.x * axes.Up.x) + Mathf.Abs(e.y * axes.Up.y) + Mathf.Abs(e.z * axes.Up.z);
            return _hullExtentUp;
        }

        /// <summary>
        /// Casts straight down from the centre of mass, ignoring the plane's own colliders,
        /// and returns whether the plane is on (or just above) the ground.
        /// </summary>
        public bool Probe(in PlaneAxes axes)
        {
            _grounded = false;
            if (_body == null)
                return false;

            Rigidbody rb = _body.Rigidbody;
            Vector3 origin = rb.worldCenterOfMass;
            float maxDist = HullExtentUp(axes) + _groundCheck;

            int count = Physics.RaycastNonAlloc(origin, -axes.Up, _hitBuffer, maxDist,
                _groundMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (_hitBuffer[i].rigidbody == rb)
                    continue;           // skip self-hits
                _grounded = true;
                break;
            }
            return _grounded;
        }

        public void Tick(in PlaneControlState c, PlaneModifierState mod, in PlaneAxes axes, float dt)
        {
            if (_body == null)
                return;

            Rigidbody rb = _body.Rigidbody;
            float fwd = Vector3.Dot(rb.linearVelocity, axes.Forward);

            // Nose-wheel steering: yaw torque, scaled by speed and reversed when rolling back.
            float steerAuth = Mathf.Clamp01(Mathf.Abs(fwd) / 5f) * (fwd < 0f ? -1f : 1f);
            _body.AddTorque(axes.Up * (c.Steer * _steerTorque * steerAuth));

            // Brake + rolling resistance along the ground-plane velocity.
            Vector3 flatVel = Vector3.ProjectOnPlane(rb.linearVelocity, axes.Up);
            if (flatVel.sqrMagnitude > 0.01f)
                _body.AddForce(-flatVel.normalized * (_rollingResistance + c.Brake * _brakeForce));

            // Lateral grip: cancel sideways slide so it tracks like wheels (we run friction-less).
            float lateral = Vector3.Dot(rb.linearVelocity, axes.Right);
            _body.AddForce(-axes.Right * (lateral * _lateralGrip * dt));

            // Keep upright — ROLL ONLY, so it never fights you raising the nose for take-off.
            Vector3 levelAxis = Vector3.Cross(axes.Up, Vector3.up);
            float rollErr = Vector3.Dot(levelAxis, axes.Forward);
            _body.AddTorque(axes.Forward * (rollErr * _uprightTorque));
        }
    }
}
