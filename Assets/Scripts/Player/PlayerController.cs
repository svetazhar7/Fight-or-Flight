using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float jumpHeight = 2f;
    public float gravity = -20f;

    [Header("Look")]
    public float mouseSensitivity = 150f;

    [Header("References")]
    public Transform headPivot;

    private CharacterController controller;
    private PlayerInputActions input;

    private Vector2 moveInput;
    private Vector2 lookInput;

    private Vector3 velocity;

    private float pitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner)
            return;

        input = new PlayerInputActions();

        input.Player.Move.performed +=
            ctx => moveInput = ctx.ReadValue<Vector2>();

        input.Player.Move.canceled +=
            ctx => moveInput = Vector2.zero;

        input.Player.Look.performed +=
            ctx => lookInput = ctx.ReadValue<Vector2>();

        input.Player.Look.canceled +=
            ctx => lookInput = Vector2.zero;

        input.Player.Jump.performed +=
            ctx => Jump();

        input.Enable();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        Move();
        Look();
    }

    private void Move()
    {
        Vector3 move =
            transform.right * moveInput.x +
            transform.forward * moveInput.y;

        controller.Move(
            move *
            moveSpeed *
            Time.deltaTime);

        if (controller.isGrounded &&
            velocity.y < 0)
        {
            velocity.y = -2f;
        }

        velocity.y +=
            gravity *
            Time.deltaTime;

        controller.Move(
            velocity *
            Time.deltaTime);
    }

    private void Jump()
    {
        if (!controller.isGrounded)
            return;

        velocity.y =
            Mathf.Sqrt(
                jumpHeight *
                -2f *
                gravity);
    }

    private void Look()
    {
        float mouseX =
            lookInput.x *
            mouseSensitivity *
            Time.deltaTime;

        float mouseY =
            lookInput.y *
            mouseSensitivity *
            Time.deltaTime;

        // Поворот тела по горизонтали
        transform.Rotate(
            Vector3.up *
            mouseX);

        // Поворот головы по вертикали
        pitch -= mouseY;

        pitch = Mathf.Clamp(
            pitch,
            -80f,
            80f);

        headPivot.localRotation =
            Quaternion.Euler(
                pitch,
                0f,
                0f);
    }

    private void OnDestroy()
    {
        input?.Dispose();
    }
}