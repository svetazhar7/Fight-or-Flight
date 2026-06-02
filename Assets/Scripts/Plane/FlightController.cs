using UnityEngine;

public class FlightController : MonoBehaviour
{
    [Header("Flight State")]
    public float speed;

    public float altitude;

    public float roll;

    public float pitch;

    public float yaw;

    [Header("Limits")]
    public float maxSpeed = 250f;

    public float maxRoll = 45f;

    public float maxPitch = 30f;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponentInParent<Rigidbody>();
    }

    private void Update()
    {
        altitude =
            transform.position.y * 3.28084f;

        roll =
            NormalizeAngle(
                transform.eulerAngles.z);

        pitch =
            NormalizeAngle(
                transform.eulerAngles.x);

        yaw =
            transform.eulerAngles.y;
    }

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return angle;
    }
}