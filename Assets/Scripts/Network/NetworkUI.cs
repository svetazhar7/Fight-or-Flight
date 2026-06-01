using FishNet;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    public void Host()
    {
        InstanceFinder.ServerManager.StartConnection();
        InstanceFinder.ClientManager.StartConnection();
    }

    public void Client()
    {
        InstanceFinder.ClientManager.StartConnection();
    }
}