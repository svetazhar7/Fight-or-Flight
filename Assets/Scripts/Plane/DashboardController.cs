using TMPro;
using UnityEngine;

public class DashboardController : MonoBehaviour
{
    [Header("References")]
    public FlightController flight;

    public Transform speedNeedle;

    public TextMeshPro altitudeText;

    public TextMeshPro rollText;

    private void Update()
    {
        UpdateSpeed();

        UpdateAltitude();

        UpdateRoll();
    }

    private void UpdateSpeed()
    {
        float angle =
            Mathf.Lerp(
                -90f,
                90f,
                flight.speed / 250f);

        speedNeedle.localRotation =
            Quaternion.Euler(
                0,
                0,
                angle);
    }

    private void UpdateAltitude()
    {
        altitudeText.text =
            $"{Mathf.RoundToInt(flight.altitude)} FT";
    }

    private void UpdateRoll()
    {
        if (flight.roll < 0)
        {
            rollText.text =
                $"LEFT {Mathf.Abs((int)flight.roll)}°";
        }
        else
        {
            rollText.text =
                $"RIGHT {(int)flight.roll}°";
        }
    }
}