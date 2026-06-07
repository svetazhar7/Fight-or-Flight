using UnityEngine;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Purely cosmetic. Spins the propeller around its local axis at a speed driven by
    /// the engine's normalized RPM (via the controller, so it works for the pilot, the
    /// host and remote passengers alike). <see cref="PropellerPart"/> sets
    /// <see cref="Enabled"/> = false when the prop is lost.
    /// </summary>
    public class PropellerAnimator : MonoBehaviour
    {
        [SerializeField] private PlaneController _controller;
        [SerializeField] private Transform _propeller;

        [Tooltip("Local spin axis of the propeller mesh.")]
        [SerializeField] private Vector3 _spinAxis = Vector3.forward;

        [Tooltip("RPM at full engine power, used to convert normalized RPM to degrees/sec.")]
        [SerializeField] private float _maxRpm = 2200f;

        public bool Enabled { get; set; } = true;

        private void Update()
        {
            if (!Enabled || _controller == null || _propeller == null)
                return;

            float degPerSec = _controller.VisualRpm01 * _maxRpm / 60f * 360f;
            _propeller.Rotate(_spinAxis, degPerSec * Time.deltaTime, Space.Self);
        }
    }
}
