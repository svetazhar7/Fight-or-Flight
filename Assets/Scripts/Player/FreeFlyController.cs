using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Free-fly camera controller (new Input System).
/// Hold RIGHT MOUSE to look around. WASD = move, Space/E = up, Ctrl/Q = down,
/// Left Shift = speed boost. Designed for roaming large procedural maps.
/// </summary>
public class FreeFlyController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Base movement speed in meters/second.")]
    public float moveSpeed = 350f;
    [Tooltip("Speed multiplier while holding Left Shift.")]
    public float boostMultiplier = 5f;

    [Header("Look")]
    public float lookSensitivity = 0.1f;
    [Tooltip("If true, you must hold the right mouse button to rotate the view.")]
    public bool holdRightMouseToLook = true;

    private float _yaw;
    private float _pitch;

    void Start()
    {
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = e.x;
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        // --- Look ---
        bool looking = !holdRightMouseToLook || (mouse != null && mouse.rightButton.isPressed);
        if (looking && mouse != null)
        {
            Vector2 d = mouse.delta.ReadValue();
            _yaw += d.x * lookSensitivity;
            _pitch -= d.y * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        // --- Move ---
        Vector3 dir = Vector3.zero;
        if (kb.wKey.isPressed) dir += transform.forward;
        if (kb.sKey.isPressed) dir -= transform.forward;
        if (kb.dKey.isPressed) dir += transform.right;
        if (kb.aKey.isPressed) dir -= transform.right;
        if (kb.spaceKey.isPressed || kb.eKey.isPressed) dir += Vector3.up;
        if (kb.leftCtrlKey.isPressed || kb.qKey.isPressed) dir -= Vector3.up;

        float speed = moveSpeed * (kb.leftShiftKey.isPressed ? boostMultiplier : 1f);
        transform.position += dir.normalized * speed * Time.deltaTime;
#endif
    }
}
