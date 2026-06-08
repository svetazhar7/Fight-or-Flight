using UnityEngine;

/// <summary>
/// Inspector-configurable fog and camera draw distance.
/// Applies to RenderSettings + the target camera in both edit and play mode.
/// </summary>
[ExecuteAlways]
public class EnvironmentSettings : MonoBehaviour
{
    [Header("Draw Distance")]
    [Tooltip("Camera far clip plane (max render distance) in meters.")]
    public float drawDistance = 5500f;

    [Tooltip("Camera to apply the draw distance to. Leave empty to use Camera.main.")]
    public Camera targetCamera;

    [Header("Fog")]
    public bool fogEnabled = true;
    public Color fogColor = new Color(0.72f, 0.80f, 0.87f);
    public FogMode fogMode = FogMode.Linear;

    [Tooltip("Linear fog: distance where fog begins.")]
    public float fogStartDistance = 1200f;

    [Tooltip("Linear fog: distance where the view is fully fogged (visibility limit).")]
    public float fogEndDistance = 5000f;

    [Tooltip("Density for Exponential / ExponentialSquared fog modes.")]
    public float fogDensity = 0.0003f;

    [Tooltip("Force the camera background to the fog color (solid). Off keeps the skybox.")]
    public bool matchCameraBackground = false;

    void OnEnable() => Apply();
    void Start() => Apply();
    void OnValidate() => Apply();

    public void Apply()
    {
        RenderSettings.fog = fogEnabled;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogStartDistance = fogStartDistance;
        RenderSettings.fogEndDistance = fogEndDistance;
        RenderSettings.fogDensity = Mathf.Max(0f, fogDensity);

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
        {
            cam.farClipPlane = Mathf.Max(cam.nearClipPlane + 1f, drawDistance);
            if (matchCameraBackground)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = fogColor;
            }
        }
    }
}
