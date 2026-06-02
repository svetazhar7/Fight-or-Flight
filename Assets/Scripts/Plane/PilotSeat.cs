using UnityEngine;

public class PilotSeat : MonoBehaviour
{
    [Header("Seat")]
    public Transform seatPoint;

    public Transform pilotViewPoint;

    private PlayerController currentPilot;

    public PlayerController CurrentPilot =>
        currentPilot;

    public bool IsOccupied =>
        currentPilot != null;

    public void Sit(PlayerController player)
    {
        if (IsOccupied)
            return;

        currentPilot = player;

        player.EnterPilotSeat(
            seatPoint,
            pilotViewPoint);
    }

    public void Leave()
    {
        if (currentPilot == null)
            return;

        currentPilot.ExitPilotSeat();

        currentPilot = null;
    }
}