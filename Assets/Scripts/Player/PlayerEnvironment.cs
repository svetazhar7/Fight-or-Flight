using DistantLands.Cozy;
using FishNet.Object;
using IslandSystem;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owner-side environment hookup for the spawned player. Networked prefabs appear at runtime, so the
/// scene systems can't reference them — this binds them when the OWNED player spawns:
///  - grass streams around this player and only builds inside this camera's frustum;
///  - the COZY weather skydome follows/scales to this camera (it was waiting for a Camera.main that
///    never existed — no camera in the project is tagged MainCamera before spawn);
///  - the camera filters weather needs are enabled (post-processing for COZY fog/filter FX);
///  - the plain scene preview camera and its AudioListener are silenced.
/// The GrassInteractor (grass flattening) lives on the prefab root for ALL peers, not just the owner.
/// </summary>
public class PlayerEnvironment : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsOwner) return;

        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);
        if (playerCamera == null) return;

        // This is THE main camera now (COZY 'useMainCamera' lock, audio, any Camera.main fallback).
        playerCamera.gameObject.tag = "MainCamera";

        // Weather filters: COZY's fog / filter / atmosphere FX apply in post-processing — must be on.
        var camData = playerCamera.GetComponent<UniversalAdditionalCameraData>();
        if (camData != null) camData.renderPostProcessing = true;

        // COZY skydome follows and scales to this camera from now on.
        var cozy = CozyWeather.instance;
        if (cozy != null) cozy.cozyCamera = playerCamera;

        // Grass: stream around the player, build only where this camera looks.
        IslandGrassField.LocalViewer = transform;
        IslandGrassField.LocalViewerCamera = playerCamera;

        // The plain scene camera (editing/preview leftover) must not render or listen any more.
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (cam == playerCamera || cam.cameraType != CameraType.Game) continue;
            if (cam.GetComponentInParent<NetworkObject>() != null) continue; // other players' cams (inactive)
            cam.gameObject.SetActive(false);
        }
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (listener.gameObject != playerCamera.gameObject) listener.enabled = false;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (!IsOwner) return;

        if (IslandGrassField.LocalViewer == transform)
        {
            IslandGrassField.LocalViewer = null;
            IslandGrassField.LocalViewerCamera = null;
        }
        var cozy = CozyWeather.instance;
        if (cozy != null && cozy.cozyCamera == playerCamera) cozy.cozyCamera = null;
    }
}
