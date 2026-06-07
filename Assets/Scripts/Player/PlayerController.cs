using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
using FightOrFlight.Aircraft;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpHeight = 2f;
    [SerializeField] private float gravity = -20f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 150f;

    [Header("Interaction")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Header("References")]
    [SerializeField] private Transform headPivot;

    private CharacterController controller;
    private PlayerInputActions input;

    private Vector2 moveInput;
    private Vector2 lookInput;

    private Vector3 velocity;

    private float pitch;
    private float pilotYaw;

    private bool isPilot;

    private PilotSeat currentSeat;
    private Camera playerCamera;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Find the player's own camera (child of HeadPivot, tagged Untagged).
        // We can't use Camera.main here — that returns the scene camera,
        // not the one that belongs to this player.
        playerCamera = GetComponentInChildren<Camera>(true);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner)
            return;

        input = new PlayerInputActions();

        input.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        input.Player.Move.canceled += ctx => moveInput = Vector2.zero;

        input.Player.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
        input.Player.Look.canceled += ctx => lookInput = Vector2.zero;

        input.Player.Jump.performed += ctx => Jump();
        input.Player.Interact.performed += ctx => Interact();

        input.Enable();

        // Flight is driven by the plane's own PlaneInputReader once the pilot owns it,
        // so the player's Plane action map is no longer used.
        input.Plane.Disable();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        // While piloting, the plane reads its own input; we only steer the cockpit camera.
        if (isPilot)
        {
            LookPilot();
            return;
        }

        Move();
        Look();
    }

    private void Move()
    {
        Vector3 move =
            transform.right * moveInput.x +
            transform.forward * moveInput.y;

        controller.Move(move * moveSpeed * Time.deltaTime);

        if (controller.isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        velocity.y += gravity * Time.deltaTime;

        controller.Move(velocity * Time.deltaTime);
    }

    private void Look()
    {
        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        transform.Rotate(Vector3.up * mouseX);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -80f, 80f);

        headPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void Jump()
    {
        if (!controller.isGrounded)
            return;

        velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    private void Interact()
    {
        if (isPilot)
        {
            ExitPilotSeat();
            return;
        }

        TryEnterSeat();
    }

    // Raycast from the camera for a PilotSeat. Event-driven (on key press), never in Update.
    private void TryEnterSeat()
    {
        if (playerCamera == null)
            return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactMask, QueryTriggerInteraction.Collide))
            return;

        PilotSeat seat = hit.collider.GetComponentInParent<PilotSeat>();
        if (seat != null)
            EnterPilotSeat(seat);
    }

    private void LookPilot()
    {
        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        pilotYaw += mouseX;
        pilotYaw = Mathf.Clamp(pilotYaw, -90f, 90f);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -80f, 80f);

        if (playerCamera != null)
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, pilotYaw, 0f);
    }

    public void EnterPilotSeat(PilotSeat seat)
    {
        if (seat == null || seat.IsOccupied)
            return;

        // Ask the server to seat us and hand over control of the plane.
        seat.RequestEnter();

        currentSeat = seat;
        isPilot = true;

        // CharacterController stays disabled while seated so physics can't push the
        // player out of the seat geometry. Re-enabled in ExitPilotSeat.
        controller.enabled = false;

        if (seat.SeatPoint != null)
        {
            transform.position = seat.SeatPoint.position;
            transform.rotation = seat.SeatPoint.rotation;
        }

        // Yield the keyboard: stop walking/jumping so WSAD/space don't move the body.
        // The plane's own PlaneInputReader picks up flight input. Look + Interact stay
        // enabled for cockpit free-look and to leave the seat.
        moveInput = Vector2.zero;
        input.Player.Move.Disable();
        input.Player.Jump.Disable();

        pilotYaw = 0f;
        pitch = 0f;

        if (playerCamera != null)
        {
            playerCamera.transform.SetParent(seat.PilotViewPoint);
            playerCamera.transform.localPosition = Vector3.zero;
            playerCamera.transform.localRotation = Quaternion.identity;
        }
    }

    public void ExitPilotSeat()
    {
        if (currentSeat == null)
            return;

        // Release the seat and return plane authority to the server.
        currentSeat.RequestExit();

        currentSeat = null;
        isPilot = false;
        pilotYaw = 0f;

        input.Player.Move.Enable();
        input.Player.Jump.Enable();

        transform.position += transform.right * 2f;
        controller.enabled = true;

        if (playerCamera != null)
        {
            playerCamera.transform.SetParent(headPivot);
            playerCamera.transform.localPosition = Vector3.zero;
            playerCamera.transform.localRotation = Quaternion.identity;
        }
    }

    private void OnDestroy()
    {
        input?.Dispose();
    }
}
