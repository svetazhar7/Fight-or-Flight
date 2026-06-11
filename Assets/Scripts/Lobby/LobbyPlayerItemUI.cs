using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FightOrFlight.Lobby
{
    /// <summary>
    /// A single row in the lobby player list. Pure presentation: it is told what
    /// to show via <see cref="Setup"/> and reports kick requests back through a
    /// callback. The host is visually highlighted (crown badge + golden frame).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyPlayerItemUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image avatarImage;
        [SerializeField] private Image highlightFrame;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text readyText;
        [SerializeField] private GameObject hostBadge;
        [SerializeField] private Button kickButton;

        [Header("Fallback")]
        [SerializeField] private Sprite defaultAvatar;

        [Header("Colours")]
        [SerializeField] private Color readyColor = new(0.36f, 0.85f, 0.40f);
        [SerializeField] private Color notReadyColor = new(0.78f, 0.78f, 0.82f);
        [SerializeField] private Color hostFrameColor = new(1f, 0.80f, 0.18f, 1f);
        [SerializeField] private Color normalFrameColor = new(1f, 1f, 1f, 0f);

        public int ClientId { get; private set; }

        private Action<int> _onKick;

        /// <summary>Populates the row from replicated data.</summary>
        public void Setup(LobbyPlayerData data, bool localIsHost, Action<int> onKick)
        {
            ClientId = data.ClientId;
            _onKick = onKick;

            if (nameText != null)
                nameText.text = data.DisplayName;

            if (statusText != null)
                statusText.text = data.IsHost ? "HOST" : "CONNECTED";

            if (readyText != null)
            {
                readyText.text = data.IsReady ? "READY" : "NOT READY";
                readyText.color = data.IsReady ? readyColor : notReadyColor;
            }

            if (hostBadge != null)
                hostBadge.SetActive(data.IsHost);

            if (highlightFrame != null)
                highlightFrame.color = data.IsHost ? hostFrameColor : normalFrameColor;

            // Kick button: visible to the host for everyone except the host itself.
            if (kickButton != null)
            {
                bool canKick = localIsHost && !data.IsHost;
                kickButton.gameObject.SetActive(canKick);
                kickButton.onClick.RemoveAllListeners();
                if (canKick)
                    kickButton.onClick.AddListener(() => _onKick?.Invoke(ClientId));
            }

            ApplyAvatar(data.SteamId);
        }

        private void ApplyAvatar(ulong steamId)
        {
            if (avatarImage == null)
                return;

            avatarImage.sprite = defaultAvatar;

            SteamIntegration.SteamManager steam = SteamIntegration.SteamManager.Instance;
            if (steam == null)
                return;

            steam.RequestAvatar(steamId, sprite =>
            {
                // The row may have been recycled/destroyed by the time Steam answers.
                if (avatarImage != null && sprite != null)
                    avatarImage.sprite = sprite;
            });
        }

        /// <summary>Light fade-in when the row first appears.</summary>
        public void PlayAppear()
        {
            if (canvasGroup == null)
                return;

            StopAllCoroutines();
            StartCoroutine(Fade(0f, 1f, null));
        }

        /// <summary>Light fade-out, then invokes <paramref name="onDone"/> (used before destroy).</summary>
        public void PlayDisappear(Action onDone)
        {
            if (canvasGroup == null)
            {
                onDone?.Invoke();
                return;
            }

            StopAllCoroutines();
            StartCoroutine(Fade(canvasGroup.alpha, 0f, onDone));
        }

        private IEnumerator Fade(float from, float to, Action onDone)
        {
            const float speed = 6f;
            float t = 0f;
            canvasGroup.alpha = from;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * speed;
                canvasGroup.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }
            canvasGroup.alpha = to;
            onDone?.Invoke();
        }
    }
}
