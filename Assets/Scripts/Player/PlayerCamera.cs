using FishNet.Object;
using UnityEngine;

public class PlayerCamera : NetworkBehaviour
{
    [SerializeField]
    private Camera playerCamera;

    public override void OnStartClient()
    {
        base.OnStartClient();

        playerCamera.gameObject.SetActive(IsOwner);
    }
}