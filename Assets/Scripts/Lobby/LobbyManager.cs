using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

namespace FightOrFlight.Lobby
{
    /// <summary>
    /// Authoritative, network-replicated state of the lobby. Lives as a scene
    /// network object inside the lobby scene and owns the roster of connected
    /// players (a <see cref="SyncList{T}"/>), the ready-check and host-only
    /// actions (kick / start).
    ///
    /// It is driven entirely by FishNet events — there is no per-frame polling.
    /// The UI subscribes to <see cref="RosterChanged"/> and never touches the
    /// network directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyManager : NetworkBehaviour
    {
        public static LobbyManager Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("Maximum number of players allowed in the lobby.")]
        [SerializeField] private int maxPlayers = 8;

        [Tooltip("Gameplay scene loaded by the future Start Game implementation.")]
        [SerializeField] private string gameSceneName = "01_Game";

        // Replicated roster. Avatars are resolved locally, so only this small data travels.
        private readonly SyncList<LobbyPlayerData> _players = new();

        // Plain snapshot rebuilt on every change for cheap, allocation-free UI reads.
        private readonly List<LobbyPlayerData> _snapshot = new();

        /// <summary>Raised on every roster change (join / leave / ready / kick).</summary>
        public event Action RosterChanged;

        public IReadOnlyList<LobbyPlayerData> Players => _snapshot;
        public int MaxPlayers => maxPlayers;

        /// <summary>True when the local machine is the host (server + client).</summary>
        public bool LocalIsHost => IsHostInitialized;

        /// <summary>Current ready state of the local player.</summary>
        public bool LocalIsReady => IsClientStarted && GetLocalReady();

        /// <summary>Friendly lobby title derived from the host's name.</summary>
        public string LobbyTitle
        {
            get
            {
                for (int i = 0; i < _snapshot.Count; i++)
                {
                    if (_snapshot[i].IsHost)
                        return $"{_snapshot[i].DisplayName}'s Lobby";
                }
                return "Steam Lobby";
            }
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerManager.OnRemoteConnectionState += Server_OnRemoteConnectionState;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerManager.OnRemoteConnectionState -= Server_OnRemoteConnectionState;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _players.OnChange += Players_OnChange;
            RebuildSnapshot();

            // Announce our Steam identity to the server as soon as we are in the lobby.
            CmdRegisterPlayer(SteamIntegration.SteamManager.LocalSteamIdValue,
                              SteamIntegration.SteamManager.LocalDisplayName);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _players.OnChange -= Players_OnChange;
        }

        // ----------------------------------------------------------- UI-facing actions

        /// <summary>Toggles the local player's ready state.</summary>
        public void ToggleReady()
        {
            if (!IsClientStarted)
                return;

            CmdSetReady(!GetLocalReady());
        }

        /// <summary>Host-only: requests the given client be kicked.</summary>
        public void RequestKick(int clientId) => CmdKick(clientId);

        /// <summary>Host-only: requests the match to start.</summary>
        public void RequestStartGame() => CmdStartGame();

        private bool GetLocalReady()
        {
            int id = LocalConnection.ClientId;
            for (int i = 0; i < _snapshot.Count; i++)
            {
                if (_snapshot[i].ClientId == id)
                    return _snapshot[i].IsReady;
            }
            return false;
        }

        // --------------------------------------------------------------- Server RPCs

        [ServerRpc(RequireOwnership = false)]
        private void CmdRegisterPlayer(ulong steamId, string displayName, NetworkConnection conn = null)
        {
            if (conn == null)
                return;

            // Guard against duplicate registration.
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId == conn.ClientId)
                    return;
            }

            var data = new LobbyPlayerData
            {
                ClientId = conn.ClientId,
                SteamId = steamId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Player {conn.ClientId}" : displayName,
                IsHost = conn.IsHost,
                IsReady = false
            };

            _players.Add(data);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdSetReady(bool ready, NetworkConnection conn = null)
        {
            if (conn == null)
                return;

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId != conn.ClientId)
                    continue;

                LobbyPlayerData data = _players[i];
                data.IsReady = ready;
                _players[i] = data; // Re-assigning the element raises the SyncList change.
                return;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdKick(int clientId, NetworkConnection conn = null)
        {
            if (conn == null || !conn.IsHost)
                return;                       // Only the host may kick.
            if (clientId == conn.ClientId)
                return;                       // The host can never kick itself.

            if (ServerManager.Clients.TryGetValue(clientId, out NetworkConnection target))
                ServerManager.Kick(target, KickReason.Unset);

            // The roster entry is removed by Server_OnRemoteConnectionState when the link drops.
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdStartGame(NetworkConnection conn = null)
        {
            if (conn == null || !conn.IsHost)
                return;                       // Only the host may start.

            StartGame();
        }

        // ------------------------------------------------------------- Server events

        private void Server_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;

            for (int i = _players.Count - 1; i >= 0; i--)
            {
                if (_players[i].ClientId == conn.ClientId)
                    _players.RemoveAt(i);
            }
        }

        /// <summary>
        /// Stub for starting the actual match. Runs on the server (host) only.
        /// Replace the body to load the gameplay scene when the match exists.
        /// </summary>
        private void StartGame()
        {
            Debug.Log("Starting game...");

            // Future implementation (server authoritative):
            //
            //   var sld = new FishNet.Managing.Scened.SceneLoadData(gameSceneName)
            //   {
            //       ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All
            //   };
            //   FishNet.InstanceFinder.SceneManager.LoadGlobalScenes(sld);
        }

        // --------------------------------------------------------------- Sync plumbing

        private void Players_OnChange(SyncListOperation op, int index, LobbyPlayerData oldItem, LobbyPlayerData newItem, bool asServer)
        {
            // Mirror the host's two callbacks (asServer true + false) into a single UI refresh.
            if (asServer && IsClientStarted)
                return;

            RebuildSnapshot();
        }

        private void RebuildSnapshot()
        {
            _snapshot.Clear();
            for (int i = 0; i < _players.Count; i++)
                _snapshot.Add(_players[i]);

            RosterChanged?.Invoke();
        }
    }
}
