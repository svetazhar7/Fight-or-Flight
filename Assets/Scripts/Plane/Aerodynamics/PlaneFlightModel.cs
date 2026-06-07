using UnityEngine;
using FishNet.Object.Prediction;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Simplified, MODEL-INDEPENDENT arcade aerodynamics.
    ///
    /// Every force and torque is applied at the rigidbody centre of mass
    /// (<see cref="PredictionRigidbody.AddForce"/> / <see cref="PredictionRigidbody.AddTorque"/>),
    /// so the flight feel does NOT depend on where any wing / propeller / mesh pivot sits.
    /// Swap the visual model freely — only the local axes on <see cref="PlaneController"/>
    /// need to match the new orientation.
    ///
    /// What it produces:
    ///   • Lift  ∝ airspeed²  along the aircraft "up", while moving forward (build speed to fly).
    ///   • Drag  ∝ speed²     opposing velocity   (caps top speed together with thrust).
    ///   • Control torques (pitch/yaw/roll) that fade in with airspeed.
    ///   • Weather-vaning + a wings-level autopilot so the plane is stable and NEVER spins
    ///     out of control with no input.
    ///
    /// Driven by <see cref="PlaneController"/> once per prediction tick — it has no Update
    /// loop of its own (so reconcile can replay it deterministically).
    /// </summary>
    public class PlaneFlightModel : MonoBehaviour
    {
        [Header("Lift / Drag — applied at the centre of mass")]
        [Tooltip("Lift (N) = this × airspeed². Higher = takes off / flies at a lower speed. " +
                 "Take-off speed ≈ sqrt(mass × 9.81 / this).")]
        [SerializeField] private float _liftPerSpeedSqr = 22f;

        [Tooltip("Upper clamp on lift (N) so high speed doesn't balloon the plane skyward, and " +
                 "so a nose-down attitude can't vector lift forward into a speed run-away. " +
                 "Keep it ~1.2–1.5× the plane's weight (mass × 9.81).")]
        [SerializeField] private float _maxLift = 32000f;

        [Tooltip("Drag (N) = this × speed². Sets the top speed together with engine thrust.")]
        [SerializeField] private float _dragPerSpeedSqr = 2f;

        [Tooltip("Hard cap on speed (m/s) — a safety net so nothing can make the plane run away.")]
        [SerializeField] private float _maxSpeed = 90f;

        [Header("Control authority (torque per axis: x=pitch, y=yaw, z=roll)")]
        [Tooltip("Tuned against the fixed inertia set by PlaneController, NOT the model mesh.")]
        [SerializeField] private Vector3 _controlPower = new Vector3(5000f, 2500f, 8000f);

        [Tooltip("Airspeed (m/s) at which controls reach full authority. Below this they fade.")]
        [SerializeField] private float _controlRefSpeed = 30f;

        [Header("Stability (keeps it flyable, kills runaway spin)")]
        [Tooltip("Weather-vaning: turns the nose toward the velocity vector. 0 = off.")]
        [SerializeField] private float _weatherVane = 100f;

        [Tooltip("Rolls the wings back toward level when the pilot isn't rolling. 0 = off.")]
        [SerializeField] private float _autoLevel = 60f;

        [Header("Stall (feel / HUD only — does not special-case the physics)")]
        [SerializeField] private float _stallAoA = 16f;

        [Header("Mass")]
        [Tooltip("Extra COM offset (airframe local) added on top of the auto-centred COM. Usually zero.")]
        [SerializeField] private Vector3 _baseCenterOfMass = Vector3.zero;

        public Vector3 BaseCenterOfMass => _baseCenterOfMass;
        public bool IsStalling { get; private set; }
        public float AirSpeed { get; private set; }

        private PredictionRigidbody _body;
        public void Bind(PredictionRigidbody body) => _body = body;

        public void Tick(in PlaneControlState c, PlaneModifierState mod,
            PlaneControlMode mode, bool grounded, in PlaneAxes axes, float dt)
        {
            if (_body == null)
                return;

            Rigidbody rb = _body.Rigidbody;
            Vector3 vel = rb.linearVelocity;
            float speed = vel.magnitude;

            // Hard safety clamp: never let the plane exceed max speed (kills any run-away,
            // e.g. lift vectoring forward in a steep dive). Cheap and unconditional.
            if (_maxSpeed > 0f && speed > _maxSpeed)
            {
                vel *= _maxSpeed / speed;
                rb.linearVelocity = vel;
                speed = _maxSpeed;
            }
            AirSpeed = speed;

            float fwdSpeed = Vector3.Dot(vel, axes.Forward);

            // 1) Lift — grows with airspeed², along the aircraft up, only while moving
            //    forward. Uses total airspeed (not the forward component) so raising the
            //    nose to rotate doesn't paradoxically shed lift. At COM => no torque.
            float lift = fwdSpeed > 0f
                ? Mathf.Min(_liftPerSpeedSqr * speed * speed, _maxLift)
                : 0f;
            _body.AddForce(axes.Up * (lift * mod.LiftMultiplier));

            // 2) Drag — opposes velocity, quadratic. At COM => no torque.
            if (speed > 0.01f)
                _body.AddForce(-(vel / speed) * (_dragPerSpeedSqr * speed * speed * mod.DragMultiplier));

            // 3) Stall flag from angle of attack (feel/HUD only).
            float vUp = Vector3.Dot(vel, axes.Up);
            float aoaDeg = Mathf.Atan2(-vUp, Mathf.Max(0.01f, fwdSpeed)) * Mathf.Rad2Deg;
            IsStalling = speed > 2f && Mathf.Abs(aoaDeg) > _stallAoA;

            // 4) Control torques about the aircraft axes — fade at low airspeed / with damage.
            float authority = Mathf.Clamp01(speed / Mathf.Max(0.01f, _controlRefSpeed)) * mod.ControlMultiplier;
            if (IsStalling)
                authority *= 0.4f;

            // Pitch is negated so c.Pitch > 0 (W) raises the nose: a +torque about the
            // starboard axis pitches the nose DOWN, which would be the inverted "yoke" feel.
            Vector3 torque =
                axes.Right * (-c.Pitch * _controlPower.x) +
                axes.Up * (c.Yaw * _controlPower.y) +
                axes.Forward * (-c.Roll * _controlPower.z);
            _body.AddTorque(torque * authority);

            // 5) Passive stability — only airborne, only with airflow. Both are stable
            //    attractors (toward velocity / toward level) so they can't drive a spin.
            if (!grounded && speed > 1f)
            {
                // Saturate the stabilizers at the control reference speed so they stay a
                // GENTLE background pull and never overpower the pilot at high speed (an
                // unbounded weather-vane pins the nose to the velocity and you lose control).
                float stabSpeed = Mathf.Min(speed, _controlRefSpeed);
                Vector3 fwdDir = vel / speed;

                // Weather-vane: nudge the nose toward the velocity vector (kills sideslip).
                Vector3 vane = Vector3.Cross(axes.Forward, fwdDir);
                _body.AddTorque(vane * (_weatherVane * stabSpeed) * mod.ControlMultiplier);

                // Wings-level autopilot when the pilot isn't rolling. Sign-correct by
                // construction: Cross(up, worldUp) is the axis that rotates up→worldUp;
                // its component along Forward is exactly the roll error.
                if (_autoLevel > 0f && Mathf.Abs(c.Roll) < 0.05f)
                {
                    Vector3 levelAxis = Vector3.Cross(axes.Up, Vector3.up);
                    float rollErr = Vector3.Dot(levelAxis, axes.Forward);
                    _body.AddTorque(axes.Forward * (rollErr * _autoLevel * stabSpeed) * authority);
                }
            }
        }
    }
}
