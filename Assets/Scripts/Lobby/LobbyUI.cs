using System.Collections;
using System.Collections.Generic;
using FightOrFlight.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FightOrFlight.Lobby
{
    /// <summary>
    /// Drives the lobby screen. Holds no network logic: it listens to
    /// <see cref="LobbyManager.RosterChanged"/> and rebuilds the player list with
    /// a light pooled diff so rows fade in/out instead of flickering.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private RectTransform panelRect;

        [Header("Header texts")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text lobbyNameText;
        [SerializeField] private TMP_Text playerCountText;

        [Header("Player list")]
        [SerializeField] private RectTransform listContent;
        [SerializeField] private LobbyPlayerItemUI playerItemPrefab;

        [Header("Buttons")]
        [SerializeField] private Button inviteButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonLabel;

        [Header("Content")]
        [SerializeField] private string titleString = "LOBBY";

        private readonly Dictionary<int, LobbyPlayerItemUI> _rows = new();
        private LobbyManager _lobby;
        private bool _subscribed;

        private void Start()
        {
            if (titleText != null)
                titleText.text = titleString;

            WireButtons();
            PlayPanelIntro();
            TryBind();
        }

        private void OnDestroy() => Unsubscribe();

        private void WireButtons()
        {
            if (inviteButton != null)
                inviteButton.onClick.AddListener(() => SessionManager.Instance?.OpenInviteOverlay());

            if (leaveButton != null)
                leaveButton.onClick.AddListener(() => SessionManager.Instance?.LeaveSession());

            if (startButton != null)
                startButton.onClick.AddListener(() => _lobby?.RequestStartGame());

            if (readyButton != null)
                readyButton.onClick.AddListener(() => _lobby?.ToggleReady());
        }

        // The LobbyManager is a networked scene object; it may not have spawned yet
        // on the first frame. Poll briefly (not every frame, just until bound) then
        // switch fully to event-driven updates.
        private void TryBind()
        {
            _lobby = LobbyManager.Instance;
            if (_lobby == null)
            {
                StartCoroutine(WaitForLobby());
                return;
            }

            Subscribe();
            Refresh();
        }

        private IEnumerator WaitForLobby()
        {
            while (LobbyManager.Instance == null)
                yield return null;

            _lobby = LobbyManager.Instance;
            Subscribe();
            Refresh();
        }

        private void Subscribe()
        {
            if (_subscribed || _lobby == null)
                return;

            _lobby.RosterChanged += Refresh;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _lobby == null)
                return;

            _lobby.RosterChanged -= Refresh;
            _subscribed = false;
        }

        private void Refresh()
        {
            if (_lobby == null)
                return;

            IReadOnlyList<LobbyPlayerData> roster = _lobby.Players;
            bool localIsHost = _lobby.LocalIsHost;

            // Header.
            if (lobbyNameText != null)
                lobbyNameText.text = _lobby.LobbyTitle;
            if (playerCountText != null)
                playerCountText.text = $"Players: {roster.Count} / {_lobby.MaxPlayers}";

            // Host-only controls.
            if (startButton != null)
                startButton.gameObject.SetActive(localIsHost);

            if (readyButtonLabel != null)
                readyButtonLabel.text = _lobby.LocalIsReady ? "READY ✓" : "READY UP";

            UpdateRows(roster, localIsHost);
        }

        private void UpdateRows(IReadOnlyList<LobbyPlayerData> roster, bool localIsHost)
        {
            // Add / update.
            var present = new HashSet<int>();
            for (int i = 0; i < roster.Count; i++)
            {
                LobbyPlayerData data = roster[i];
                present.Add(data.ClientId);

                if (!_rows.TryGetValue(data.ClientId, out LobbyPlayerItemUI row))
                {
                    row = Instantiate(playerItemPrefab, listContent);
                    _rows[data.ClientId] = row;
                    row.Setup(data, localIsHost, OnKickRequested);
                    row.PlayAppear();
                }
                else
                {
                    row.Setup(data, localIsHost, OnKickRequested);
                }

                // Preserve roster order.
                row.transform.SetSiblingIndex(i);
            }

            // Remove rows that left.
            if (_rows.Count == present.Count)
                return;

            var leaving = new List<int>();
            foreach (KeyValuePair<int, LobbyPlayerItemUI> kvp in _rows)
            {
                if (!present.Contains(kvp.Key))
                    leaving.Add(kvp.Key);
            }

            foreach (int clientId in leaving)
            {
                LobbyPlayerItemUI row = _rows[clientId];
                _rows.Remove(clientId);
                if (row != null)
                    row.PlayDisappear(() => { if (row != null) Destroy(row.gameObject); });
            }
        }

        private void OnKickRequested(int clientId) => _lobby?.RequestKick(clientId);

        private void PlayPanelIntro()
        {
            if (panelGroup == null)
                return;

            StopAllCoroutines();
            StartCoroutine(PanelIntro());
        }

        private IEnumerator PanelIntro()
        {
            const float speed = 3.5f;
            float t = 0f;
            Vector3 targetScale = panelRect != null ? panelRect.localScale : Vector3.one;
            Vector3 startScale = targetScale * 0.96f;

            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * speed;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f); // ease-out cubic
                panelGroup.alpha = e;
                if (panelRect != null)
                    panelRect.localScale = Vector3.LerpUnclamped(startScale, targetScale, e);
                yield return null;
            }

            panelGroup.alpha = 1f;
            if (panelRect != null)
                panelRect.localScale = targetScale;
        }
    }
}
