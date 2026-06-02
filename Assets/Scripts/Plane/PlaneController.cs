using UnityEngine;

public class PlaneController : MonoBehaviour
{
    [Header("References")]
    public PilotSeat pilotSeat;

    public FlightController flightController;

    public DashboardController dashboardController;

    public Transform pilotViewPoint;

    public Rigidbody rb;

    public bool HasPilot =>
        pilotSeat != null &&
        pilotSeat.CurrentPilot != null;
}