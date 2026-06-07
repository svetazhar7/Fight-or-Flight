using UnityEngine;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Purely cosmetic. Tilts the ControlStick fore/aft with pitch and rotates the
    /// ControlWheel left/right with roll, reading the controller's visual values
    /// (zero-latency local input for the pilot, synced for passengers). Angle limits
    /// are serialized. The wheel is a child of the stick, so it inherits the tilt.
    /// </summary>
    public class CockpitControlAnimator : MonoBehaviour
    {
        [SerializeField] private PlaneController _controller;

        [Tooltip("The stick/column that tilts forward (dive) and back (climb).")]
        [SerializeField] private Transform _controlStick;

        [Tooltip("The wheel/yoke that rotates left/right. Child of the stick.")]
        [SerializeField] private Transform _controlWheel;

        [Header("Limits (degrees)")]
        [SerializeField] private float _maxStickTilt = 20f;
        [SerializeField] private float _maxWheelAngle = 90f;

        [Header("Smoothing")]
        [SerializeField] private float _smooth = 8f;

        [Header("Local axes (adjust to your mesh orientation)")]
        [SerializeField] private Vector3 _stickTiltAxis = Vector3.right; // pitch axis
        [SerializeField] private Vector3 _wheelSpinAxis = Vector3.forward; // roll axis

        private Quaternion _stickRest;
        private Quaternion _wheelRest;

        private void Awake()
        {
            if (_controlStick != null)
                _stickRest = _controlStick.localRotation;
            if (_controlWheel != null)
                _wheelRest = _controlWheel.localRotation;
        }

        private void Update()
        {
            if (_controller == null)
                return;

            float t = _smooth * Time.deltaTime;

            if (_controlStick != null)
            {
                Quaternion target = _stickRest *
                    Quaternion.AngleAxis(_controller.VisualPitch * _maxStickTilt, _stickTiltAxis);
                _controlStick.localRotation = Quaternion.Slerp(_controlStick.localRotation, target, t);
            }

            if (_controlWheel != null)
            {
                Quaternion target = _wheelRest *
                    Quaternion.AngleAxis(-_controller.VisualRoll * _maxWheelAngle, _wheelSpinAxis);
                _controlWheel.localRotation = Quaternion.Slerp(_controlWheel.localRotation, target, t);
            }
        }
    }
}
