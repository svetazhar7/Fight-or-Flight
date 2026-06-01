using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : NetworkBehaviour
{
    [SerializeField]
    private float speed = 5f;

    private CharacterController controller;

    private PlayerInputActions input;

    private Vector2 moveInput;

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

        input.Enable();
    }

    private void Awake()
    {
        controller =
            GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        Vector3 move =
            transform.right * moveInput.x +
            transform.forward * moveInput.y;

        controller.Move(
            move *
            speed *
            Time.deltaTime);
    }

    private void OnDestroy()
    {
        input?.Dispose();
    }
}