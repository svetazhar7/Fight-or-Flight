using UnityEngine;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Reads flight input from the project's generated PlayerInputActions "Plane" map
    /// (W/S = Pitch, A/D = Roll, E = ThrottleUp, Q = ThrottleDown) and exposes a
    /// mode-independent raw snapshot. Lives on the plane; only the owning pilot's
    /// client actually consumes it (see PlaneController.IsOwner). Contains no gameplay
    /// logic — PlaneController maps these axes onto throttle/pitch/roll/steer per mode.
    /// </summary>
    public class PlaneInputReader : MonoBehaviour
    {
        private PlayerInputActions _actions;

        private void Awake() => _actions = new PlayerInputActions();
        private void OnEnable() => _actions?.Plane.Enable();
        private void OnDisable() => _actions?.Plane.Disable();
        private void OnDestroy() => _actions?.Dispose();

        public PlaneRawInput Read()
        {
            if (_actions == null)
                _actions = new PlayerInputActions();

            return new PlaneRawInput
            {
                Horizontal = _actions.Plane.Roll.ReadValue<float>(),   // A/D
                Vertical = _actions.Plane.Pitch.ReadValue<float>(),    // W/S
                ActionQ = _actions.Plane.ThrottleDown.IsPressed(),     // Q
                ActionE = _actions.Plane.ThrottleUp.IsPressed()        // E
            };
        }
    }
}
