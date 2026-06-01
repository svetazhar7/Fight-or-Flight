using FishNet.Object;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    public float speed = 5f;

    private CharacterController controller;

    public override void OnStartClient()
    {
        base.OnStartClient();

        controller = GetComponent<CharacterController>();

        Camera cam = GetComponentInChildren<Camera>();

        if (cam != null)
            cam.gameObject.SetActive(IsOwner);
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 move =
            transform.right * h +
            transform.forward * v;

        controller.Move(
            move *
            speed *
            Time.deltaTime);
    }
}