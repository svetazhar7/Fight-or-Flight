using System;
using FishNet;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using Steamworks;
using UnityEngine;

namespace FightOrFlight.Networking
{
    /// <summary>
    /// Orchestrates a play session: owns the Steam matchmaking lobby lifecycle,
    /// drives the FishNet connection (host / client) and moves everyone between
    /// the menu, the lobby and the game using FishNet global scenes.
    ///
    /// Persists across scene loads. Lives from the main menu onwards so the
    /// Steam lobby can be created before the networked lobby scene exists.
    ///
    /// Everything works without Steam too: <see cref="HostLobby"/> simply starts
    /// a local host and <see cref="JoinLocal"/> connects to the configured
    /// transport address, which makes the whole flow testable in the editor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionManager : MonoBehaviour
    {
        public static SessionManager Instance { get; private set; }

        [Header("Scenes (must be in Build Settings)")]
        [SerializeField] private string menuSceneName = "00_MainMenu";
        [SerializeField] private string lobbySceneName = "02_Lobby";

        [Header("Steam Lobby")]
        [SerializeField] private int maxLobbyMembers = 8;

        /// <summary>Raised whenever the high level session status text changes (for menu UI).</summary>
        public event Action<string> StatusChanged;

        public CSteamID CurrentLobby { get; private set; } = CSteamID.Nil;
        public bool InLobby => CurrentLobby != CSteamID.Nil;

        // Lobby metadata key used to advertise the host so invited friends know who to reach.
        private const string HostSteamIdKey = "host_steamid";

        // Steam matchmaking callbacks. Only created when Steam is available.
        private Callback<LobbyCreated_t> _lobbyCreated;
        private Callback<LobbyEnter_t> _lobbyEntered;
        private Callback<GameLobbyJoinRequested_t> _joinRequested;

        private bool _loadLobbyOnServerStart;
        private bool _subscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            SubscribeToFishNet();

            if (SteamManagerInitialized)
            {
                _lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
                _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
                _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            }
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            UnsubscribeFromFishNet();
            Instance = null;
        }

        // Indirection so this file does not hard-depend on the Steam namespace being initialised.
        private static bool SteamManagerInitialized => SteamIntegration.SteamManager.Initialized;

        // ----------------------------------------------------------------- Public API

        /// <summary>Creates a Steam lobby (if Steam is up) and starts hosting.</summary>
        public void HostLobby()
        {
            SetStatus("Creating lobby...");

            if (SteamManagerInitialized)
            {
                // The host is started in OnLobbyCreated once Steam returns the lobby id.
                SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxLobbyMembers);
            }
            else
            {
                StartHost();
            }
        }

        /// <summary>Connects to the configured transport address (local play / LAN testing).</summary>
        public void JoinLocal()
        {
            SetStatus("Connecting...");
            StartClient();
        }

        /// <summary>Opens the native Steam overlay invite dialog for the current lobby.</summary>
        public void OpenInviteOverlay()
        {
            if (SteamManagerInitialized && InLobby)
            {
                SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);
            }
            else
            {
                Debug.LogWarning("[SessionManager] Cannot open the invite overlay — Steam is not running or there is no active lobby.");
            }
        }

        /// <summary>
        /// Leaves the session for everyone the local player controls:
        /// a client disconnects; a host stops the server (which disconnects all
        /// clients). Either way we leave the Steam lobby and return to the menu.
        /// </summary>
        public void LeaveSession()
        {
            bool wasServer = InstanceFinder.IsServerStarted;

            if (SteamManagerInitialized && InLobby)
            {
                SteamMatchmaking.LeaveLobby(CurrentLobby);
                CurrentLobby = CSteamID.Nil;
            }

            if (InstanceFinder.IsClientStarted)
                InstanceFinder.ClientManager.StopConnection();

            if (wasServer)
                InstanceFinder.ServerManager.StopConnection(true);

            // Networking is down, so this is a plain local scene load.
            UnityEngine.SceneManagement.SceneManager.LoadScene(menuSceneName);
        }

        // ------------------------------------------------------------- Steam callbacks

        private void OnLobbyCreated(LobbyCreated_t cb)
        {
            if (cb.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogError($"[SessionManager] Steam lobby creation failed: {cb.m_eResult}");
                SetStatus("Lobby creation failed.");
                return;
            }

            CurrentLobby = new CSteamID(cb.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(CurrentLobby, HostSteamIdKey, SteamIntegration.SteamManager.LocalSteamIdValue.ToString());

            StartHost();
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t cb)
        {
            // Fired when the player accepts an invite or clicks "Join game" in the Steam overlay.
            SetStatus("Joining lobby...");
            SteamMatchmaking.JoinLobby(cb.m_steamIDLobby);
        }

        private void OnLobbyEntered(LobbyEnter_t cb)
        {
            CurrentLobby = new CSteamID(cb.m_ulSteamIDLobby);

            // The host created and already started the session.
            if (InstanceFinder.IsServerStarted)
                return;

            // Joining client. With a Steam P2P transport you would point the client
            // at the advertised host here:
            //
            //   string hostId = SteamMatchmaking.GetLobbyData(CurrentLobby, HostSteamIdKey);
            //   ((FishySteamworks)transport).SetClientAddress(hostId);
            //
            // Using Tugboat (IP) we connect to the transport's configured address.
            StartClient();
        }

        // ------------------------------------------------------------- FishNet control

        private void SubscribeToFishNet()
        {
            if (_subscribed || InstanceFinder.ServerManager == null)
                return;

            InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _subscribed = true;
        }

        private void UnsubscribeFromFishNet()
        {
            if (!_subscribed || InstanceFinder.ServerManager == null)
                return;

            InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _subscribed = false;
        }

        private void StartHost()
        {
            SetStatus("Starting host...");
            _loadLobbyOnServerStart = true;

            // Make sure we are subscribed even if Start ran before the NetworkManager existed.
            SubscribeToFishNet();

            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
        }

        private void StartClient()
        {
            InstanceFinder.ClientManager.StartConnection();
            // The server tells joining clients to load the global lobby scene automatically.
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started || !_loadLobbyOnServerStart)
                return;

            _loadLobbyOnServerStart = false;

            // Move every connection (including the host) into the lobby scene.
            var sld = new SceneLoadData(lobbySceneName) { ReplaceScenes = ReplaceOption.All };
            InstanceFinder.SceneManager.LoadGlobalScenes(sld);
            SetStatus("In lobby.");
        }

        private void SetStatus(string status)
        {
            StatusChanged?.Invoke(status);
        }
    }
}
