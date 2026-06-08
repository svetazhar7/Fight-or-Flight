using UnityEngine;
using TMPro;
using FightOrFlight.Aircraft;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Drives the physical cockpit dashboard displays (SpeedText, AltitudeText, RollText
    /// — all TextMeshPro world-space objects placed on the dashboard mesh in the scene).
    ///
    /// Reads telemetry from the PlaneController on the parent Body, so it works for every
    /// client (the rigidbody state is network-synced). Assign references in the Inspector
    /// or auto-wired via the editor helper below.
    /// </summary>
    public class PlaneDashboard : MonoBehaviour
    {
        [SerializeField] private PlaneController _plane;

        [Header("Display texts (TMP world-space)")]
        [SerializeField] private TextMeshPro _speedText;
        [SerializeField] private TextMeshPro _altitudeText;
        [SerializeField] private TextMeshPro _throttleText;   // the "RollText" TMP on RollScreen

        [Header("Analog needle (optional)")]
        [Tooltip("Rotates around local Z to show speed. Leave empty to skip.")]
        [SerializeField] private Transform _speedNeedle;
        [SerializeField] private float _needleMinDeg  = -120f;  // angle at 0 km/h
        [SerializeField] private float _needleMaxDeg  =  120f;  // angle at _needleMaxKmh
        [SerializeField] private float _needleMaxKmh  =  300f;

        private void Awake()
        {
            if (_plane == null)
                _plane = GetComponentInParent<PlaneController>();
        }

        private void Update()
        {
            if (_plane == null) return;

            float speedKmh = Mathf.Max(0f, _plane.AirSpeed) * 3.6f;
            float altM     = _plane.transform.position.y;
            float thr      = _plane.Throttle * 100f;

            if (_speedText != null)
                _speedText.text = $"<size=110%><b>{speedKmh:0}</b></size>\n<size=60%>km/h</size>";

            if (_altitudeText != null)
                _altitudeText.text = $"<size=110%><b>{altM:0}</b></size>\n<size=60%>m ALT</size>";

            if (_throttleText != null)
            {
                string mode = _plane.Mode == PlaneControlMode.Ground ? "GND" : "FLY";
                _throttleText.text = $"<size=110%><b>{thr:0}%</b></size>\n<size=60%>THR · {mode}</size>";
            }

            // Rotate the analog needle around its local Z axis.
            if (_speedNeedle != null)
            {
                float t = Mathf.Clamp01(speedKmh / Mathf.Max(1f, _needleMaxKmh));
                float angle = Mathf.Lerp(_needleMinDeg, _needleMaxDeg, t);
                Vector3 e = _speedNeedle.localEulerAngles;
                _speedNeedle.localEulerAngles = new Vector3(e.x, e.y, angle);
            }
        }
    }
}
