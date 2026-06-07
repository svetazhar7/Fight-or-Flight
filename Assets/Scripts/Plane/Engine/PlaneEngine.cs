using UnityEngine;
using FishNet.Object.Prediction;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Engine: integrates throttle, produces thrust along the aircraft nose axis, tracks
    /// a normalized RPM for visuals/audio. No update loop of its own — PlaneController
    /// drives <see cref="Tick"/> from inside the prediction tick.
    /// </summary>
    public class PlaneEngine : MonoBehaviour
    {
        [SerializeField] private PlaneEngineConfig _config;

        [Tooltip("Optional, COSMETIC only (e.g. spawn a prop blur here). Thrust is applied at the " +
                 "centre of mass along the aircraft nose axis, so its position never causes a turn.")]
        [SerializeField] private Transform _thrustPoint;

        private PredictionRigidbody _body;
        private float _throttle; // 0..1, simulation state (reconciled by controller)
        private float _rpm01;    // 0..1, visual only

        public float Throttle => _throttle;
        public float NormalizedRpm => _rpm01;
        public float CurrentThrust { get; private set; }
        public bool IsRunning { get; set; } = true;

        public void Bind(PredictionRigidbody body) => _body = body;
        public void SetThrottle(float value) => _throttle = Mathf.Clamp01(value);

        public void Tick(in PlaneControlState c, PlaneModifierState mod, in PlaneAxes axes, float dt)
        {
            if (_config == null || _body == null)
                return;

            _throttle = Mathf.Clamp01(_throttle + c.ThrottleDelta * _config.ThrottleChangeRate * dt);

            float boost = (c.Boost && _config.AllowBoost) ? _config.BoostMultiplier : 1f;
            float running = IsRunning ? 1f : 0f;
            CurrentThrust = _throttle * _config.MaxThrust * boost * running * mod.ThrustMultiplier;

            // Apply at the centre of mass (AddForce, not AddForceAtPosition): a propeller hub
            // offset from the COM would otherwise create a constant yaw/roll torque and send
            // the plane round in circles. Direction is the aircraft nose axis.
            if (CurrentThrust > 0f)
                _body.AddForce(axes.Forward * CurrentThrust);

            float targetRpm = IsRunning ? Mathf.Lerp(_config.IdleRpm01, 1f, _throttle) * boost : 0f;
            _rpm01 = Mathf.MoveTowards(_rpm01, targetRpm, _config.RpmSpool * dt);
        }
    }
}
