using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// First-person walking controller (CharacterController + new Input System).
/// WASD = move, Mouse = look, Space = jump, Left Shift = run, Esc = free/lock cursor.
/// Spawns on top of the active Terrain so it works with the procedural map.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class WalkController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    public float runMultiplier = 2.5f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;

    [Header("Look")]
    public float lookSensitivity = 0.1f;
    [Tooltip("Camera transform that gets pitched up/down.")]
    public Transform cameraPivot;

    [Header("Spawn")]
    [Tooltip("Snap onto the active Terrain surface on start.")]
    public bool snapToTerrainOnStart = true;

    private CharacterController _cc;
    private float _yaw;
    private float _pitch;
    private float _verticalVel;
    private bool _cursorLocked;

    void Start()
    {
        _cc = GetComponent<CharacterController>();
        _yaw = transform.eulerAngles.y;
        if (cameraPivot != null) _pitch = cameraPivot.localEulerAngles.x;

        if (snapToTerrainOnStart)
            SnapToTerrain();

        SetCursor(true);
    }

    void SnapToTerrain()
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return;

        Vector3 p = transform.position;
        float surfaceY = terrain.SampleHeight(p) + terrain.transform.position.y;
        _cc.enabled = false;
        // Controller origin == feet (center.y == height/2), so spawn just above the surface.
        transform.position = new Vector3(p.x, surfaceY + 0.2f, p.z);
        _cc.enabled = true;
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame)
            SetCursor(!_cursorLocked);

        // --- Look ---
        if (_cursorLocked && mouse != null)
        {
            Vector2 d = mouse.delta.ReadValue();
            _yaw += d.x * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - d.y * lookSensitivity, -89f, 89f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // --- Move ---
        float ix = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float iz = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        Vector3 move = Vector3.ClampMagnitude(transform.right * ix + transform.forward * iz, 1f);
        float speed = walkSpeed * (kb.leftShiftKey.isPressed ? runMultiplier : 1f);

        if (_cc.isGrounded)
        {
            _verticalVel = -2f;
            if (kb.spaceKey.wasPressedThisFrame)
                _verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else
        {
            _verticalVel += gravity * Time.deltaTime;
        }

        Vector3 velocity = move * speed + Vector3.up * _verticalVel;
        _cc.Move(velocity * Time.deltaTime);
#endif
    }

    void SetCursor(bool locked)
    {
        _cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
