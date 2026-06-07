using UnityEngine;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using FishNet.Utility.Template;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// The plane's brain. Owns the predicted rigidbody, runs the deterministic tick
    /// loop (FishNet Prediction V2), interprets input per control mode, drives every
    /// subsystem IN ORDER inside the replicate, and reconciles authoritative state.
    ///
    /// The aircraft basis (nose/lift/starboard) is derived from the rigidbody rotation
    /// and the serialized local axes — NOT transform.forward — so a rotated model pivot
    /// or non-uniform scale on the airframe doesn't break the flight math.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlaneController : TickNetworkBehaviour
    {
        [Header("Subsystems (assign in Inspector)")]
        [SerializeField] private PlaneInputReader _input;
        [SerializeField] private PlaneEngine _engine;
        [SerializeField] private PlaneFlightModel _flight;
        [SerializeField] private PlaneGroundModel _ground;
        [SerializeField] private PlaneDamageSystem _damage;

        [Header("Aircraft axes (in airframe LOCAL space)")]
        [Tooltip("Local axis pointing out the nose. Default model faces +Z; this rig uses (0,-1,0).")]
        [SerializeField] private Vector3 _localForward = new Vector3(0f, 0f, 1f);
        [Tooltip("Local axis pointing up (lift). Default (0,1,0); this rig uses (0,0,1).")]
        [SerializeField] private Vector3 _localUp = new Vector3(0f, 1f, 0f);

        [Header("Mode transition")]
        [Tooltip("Vr — minimum forward speed (m/s) before the nose can be raised on the ground.")]
        [SerializeField] private float _rotationSpeed = 28f;
        [SerializeField] private int _airborneTicksToFly = 8;
        [SerializeField] private int _groundedTicksToLand = 10;

        [Header("Rigidbody (re-applied on Awake — model independent)")]
        [Tooltip("Caps spin rate (rad/s). Stops any leftover wobble from becoming an endless spin.")]
        [SerializeField] private float _maxAngularVelocity = 12f;
        [SerializeField] private float _linearDamping = 0.02f;
        [Tooltip("Bleeds off rotation so the plane settles instead of spinning. Higher = steadier.")]
        [SerializeField] private float _angularDamping = 1.5f;
        [Tooltip("Fixed rotational inertia (kg·m², per axis) so handling is identical whatever " +
                 "mesh/scale the model imports with. Lower = more responsive. Uniform = predictable.")]
        [SerializeField] private Vector3 _inertiaTensor = new Vector3(2000f, 2000f, 2000f);

        private readonly SyncVar<float> _netPitch = new();
        private readonly SyncVar<float> _netRoll = new();
        private readonly SyncVar<float> _netRpm01 = new();

        private readonly PlaneModifierState _modifiers = new();
        private readonly PredictionRigidbody _body = new();
        private Rigidbody _rb;

        private PlaneControlMode _mode = PlaneControlMode.Ground;
        private bool _grounded = true;
        private int _airborneCounter;
        private int _groundedCounter;
        private PlaneControlState _lastControl;
        private PlaneAxes _axes;
        private Vector3 _autoComLocal;

        public PlaneControlMode Mode => _mode;
        public PlaneAxes Axes => _axes;
        public bool IsStalling => _flight != null && _flight.IsStalling;
        public float ForwardSpeed => _rb != null ? Vector3.Dot(_rb.linearVelocity, _axes.Forward) : 0f;

        public float VisualPitch => IsOwner ? _lastControl.Pitch : _netPitch.Value;
        public float VisualRoll => IsOwner ? _lastControl.Roll : _netRoll.Value;
        public float VisualRpm01 =>
            (IsOwner || IsServerStarted) && _engine != null ? _engine.NormalizedRpm : _netRpm01.Value;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // Model-independent rigidbody setup. Re-applied here in code so that swapping the
            // visual model (which re-imports with its own scale / interpolation / inertia)
            // always yields the same physics. Forces are applied at the COM by every
            // subsystem, so handling depends on these numbers, not on the mesh.
            _rb.maxAngularVelocity = _maxAngularVelocity;
            _rb.linearDamping = _linearDamping;
            _rb.angularDamping = _angularDamping;
            _rb.interpolation = RigidbodyInterpolation.None;          // prediction does the smoothing
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.inertiaTensorRotation = Quaternion.identity;
            _rb.inertiaTensor = _inertiaTensor;

            // Auto-centre the COM on the airframe collider, so balance doesn't depend on
            // wherever the imported pivot landed. Per tick we add the flight + cargo offsets.
            Collider hull = GetComponent<Collider>();
            _autoComLocal = hull != null ? transform.InverseTransformPoint(hull.bounds.center) : Vector3.zero;

            // Friction-less hull so taxiing slides instead of fighting thrust with belly
            // friction; PlaneGroundModel's lateral grip keeps it tracking straight. Applied
            // to whatever collider the airframe has, so it's model-independent.
            if (hull != null)
            {
                hull.sharedMaterial = new PhysicsMaterial("PlaneSlick")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }

            _axes = PlaneAxes.From(_rb, _localForward, _localUp);

            _body.Initialize(_rb);
            if (_engine != null) _engine.Bind(_body);
            if (_flight != null) _flight.Bind(_body);
            if (_ground != null) _ground.Bind(_body);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        protected override void TimeManager_OnTick() => RunSimulation(BuildInput());
        protected override void TimeManager_OnPostTick() => CreateReconcile();

        private PlaneReplicateData BuildInput()
        {
            if (!IsOwner || _input == null) return default;
            PlaneRawInput raw = _input.Read();
            return new PlaneReplicateData(raw.Horizontal, raw.Vertical, raw.ActionQ, raw.ActionE);
        }

        [Replicate]
        private void RunSimulation(PlaneReplicateData d,
            ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            float dt = (float)base.TimeManager.TickDelta;

            // Fresh orthonormal aircraft basis from the rigidbody rotation (scale/pivot-proof).
            _axes = PlaneAxes.From(_rb, _localForward, _localUp);

            _modifiers.Reset();
            if (_damage != null) _damage.ComposeModifiers(_modifiers);
            Vector3 flightCom = _flight != null ? _flight.BaseCenterOfMass : Vector3.zero;
            _rb.centerOfMass = _autoComLocal + flightCom + _modifiers.CenterOfMassOffset;

            PlaneControlState c = InterpretInput(d, _mode);
            _lastControl = c;

            if (_engine != null) _engine.Tick(c, _modifiers, _axes, dt);

            _grounded = _ground != null && _ground.Probe(_axes);
            if (_grounded && _ground != null) _ground.Tick(c, _modifiers, _axes, dt);
            if (_flight != null) _flight.Tick(c, _modifiers, _mode, _grounded, _axes, dt);

            _body.Simulate();

            UpdateMode();

            if (IsServerStarted)
            {
                _netPitch.Value = c.Pitch;
                _netRoll.Value = c.Roll;
                _netRpm01.Value = _engine != null ? _engine.NormalizedRpm : 0f;
            }
        }

        public override void CreateReconcile()
        {
            float throttle = _engine != null ? _engine.Throttle : 0f;
            PlaneReconcileData rd = new PlaneReconcileData(_body, throttle, (byte)_mode, _grounded);
            PerformReconcile(rd);
        }

        [Reconcile]
        private void PerformReconcile(PlaneReconcileData rd, Channel channel = Channel.Unreliable)
        {
            _body.Reconcile(rd.Body);
            if (_engine != null) _engine.SetThrottle(rd.Throttle);
            _mode = (PlaneControlMode)rd.Mode;
            _grounded = rd.Grounded;
        }

        private PlaneControlState InterpretInput(in PlaneReplicateData d, PlaneControlMode mode)
        {
            PlaneControlState c = default;

            if (mode == PlaneControlMode.Ground)
            {
                c.ThrottleDelta = d.Vertical;
                c.Steer = d.Horizontal;
                c.Brake = d.ActionQ ? 1f : 0f;
                c.Boost = d.ActionE;
                c.Pitch = ForwardSpeed >= _rotationSpeed ? Mathf.Max(0f, d.Vertical) : 0f;
            }
            else
            {
                c.Pitch = d.Vertical;
                c.Roll = d.Horizontal;
                c.Yaw = d.Horizontal * 0.5f;
                c.ThrottleDelta = (d.ActionE ? 1f : 0f) - (d.ActionQ ? 1f : 0f);
            }

            return c;
        }

        private void UpdateMode()
        {
            if (_grounded) { _groundedCounter++; _airborneCounter = 0; }
            else { _airborneCounter++; _groundedCounter = 0; }

            if (_mode == PlaneControlMode.Ground && _airborneCounter >= _airborneTicksToFly)
                _mode = PlaneControlMode.Flight;
            else if (_mode == PlaneControlMode.Flight && _groundedCounter >= _groundedTicksToLand)
                _mode = PlaneControlMode.Ground;
        }
    }
}
