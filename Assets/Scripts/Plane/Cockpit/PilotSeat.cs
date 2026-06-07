using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Pilot seat. When a player claims it, the server hands them OWNERSHIP of the
    /// plane's NetworkObject so FishNet prediction runs on their client and the plane's
    /// own <see cref="PlaneInputReader"/> reads their keyboard. Standing up returns
    /// authority to the server. The player handles its own camera/position using
    /// <see cref="SeatPoint"/> / <see cref="PilotViewPoint"/>.
    ///
    /// Must be a NetworkBehaviour on the SAME NetworkObject as the PlaneController, and
    /// needs a Collider so the player's interact raycast can hit it.
    /// </summary>
    public class PilotSeat : NetworkBehaviour
    {
        [Tooltip("The plane to hand ownership of. Usually the PlaneController on this same NetworkObject.")]
        [SerializeField] private PlaneController _plane;

        [Tooltip("Where the seated player is snapped to.")]
        [SerializeField] private Transform _seatPoint;

        [Tooltip("Where the pilot camera is anchored.")]
        [SerializeField] private Transform _pilotViewPoint;

        private readonly SyncVar<bool> _occupied = new();

        public PlaneController Plane => _plane;
        public Transform SeatPoint => _seatPoint;
        public Transform PilotViewPoint => _pilotViewPoint;

        /// <summary>Server-authoritative occupancy, synced so clients can pre-check before requesting.</summary>
        public bool IsOccupied => _occupied.Value;

        /// <summary>Client → server: claim the seat and take control of the plane.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestEnter(NetworkConnection caller = null)
        {
            if (_occupied.Value || caller == null || _plane == null)
                return;

            _occupied.Value = true;
            _plane.GiveOwnership(caller); // pilot now predicts + drives the plane
        }

        /// <summary>Client → server: release the seat (only the current pilot may).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestExit(NetworkConnection caller = null)
        {
            if (_plane == null || _plane.Owner != caller)
                return;

            _occupied.Value = false;
            _plane.RemoveOwnership(); // authority back to the server; plane idles
        }

        // NOTE: if the pilot disconnects while seated, FishNet clears plane ownership
        // but _occupied stays true. Add OnOwnershipServer handling later if seats can
        // get stuck. Fine for MVP.
    }
}
