using FishNet;
using FightOrFlight.Networking;
using UnityEngine;

/// <summary>
/// Thin menu hook for starting play. Prefers the full session flow
/// (<see cref="SessionManager"/> — Steam lobby + scene transitions) when it is
/// present, and falls back to a direct FishNet connection so the legacy
/// single-scene setup keeps working when no SessionManager exists.
/// </summary>
public class NetworkUI : MonoBehaviour
{
    public void Host()
    {
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.HostLobby();
            return;
        }

        InstanceFinder.ServerManager.StartConnection();
        InstanceFinder.ClientManager.StartConnection();
    }

    public void Client()
    {
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.JoinLocal();
            return;
        }

        InstanceFinder.ClientManager.StartConnection();
    }
}
