using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace FightOrFlight.SteamIntegration
{
    /// <summary>
    /// Owns the Steamworks runtime: initialises the API, pumps callbacks every
    /// frame and exposes lightweight helpers for the local user, persona names
    /// and avatars. Persists across scene loads so the Steam session lives for
    /// the whole application lifetime.
    ///
    /// Every member degrades gracefully when Steam is not running, so the game
    /// (and the lobby) is still playable and testable without a Steam client.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamManager : MonoBehaviour
    {
        public static SteamManager Instance { get; private set; }

        /// <summary>True once <see cref="SteamAPI.Init"/> has succeeded.</summary>
        public static bool Initialized => Instance != null && Instance._initialized;

        private bool _initialized;
        private bool _triedInit;

        public static CSteamID LocalSteamId => Initialized ? SteamUser.GetSteamID() : CSteamID.Nil;
        public static ulong LocalSteamIdValue => Initialized ? SteamUser.GetSteamID().m_SteamID : 0UL;
        public static string LocalDisplayName => Initialized ? SteamFriends.GetPersonaName() : "Player";

        // Cached so the managed delegate is not garbage collected while Steam holds it.
        private SteamAPIWarningMessageHook_t _warningHook;

        // --- Avatar handling ---------------------------------------------------
        private Callback<AvatarImageLoaded_t> _avatarLoadedCallback;
        private readonly Dictionary<ulong, Sprite> _avatarCache = new();
        private readonly Dictionary<ulong, Action<Sprite>> _avatarPending = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            TryInitialize();
        }

        private void TryInitialize()
        {
            if (_triedInit)
                return;
            _triedInit = true;

            if (!Packsize.Test())
                Debug.LogError("[SteamManager] Packsize.Test failed — wrong Steamworks.NET binaries for this platform.");
            if (!DllCheck.Test())
                Debug.LogError("[SteamManager] DllCheck.Test failed — Steam DLLs are missing or the wrong version.");

            try
            {
                _initialized = SteamAPI.Init();
            }
            catch (DllNotFoundException e)
            {
                Debug.LogWarning($"[SteamManager] Steam native library not found — running without Steam. {e.Message}");
                _initialized = false;
                return;
            }

            if (!_initialized)
            {
                Debug.LogWarning("[SteamManager] SteamAPI.Init() failed. Is Steam running and is steam_appid.txt present? " +
                                 "Continuing without Steam (placeholder names/avatars).");
                return;
            }

            _warningHook = OnSteamWarning;
            SteamClient.SetWarningMessageHook(_warningHook);
            _avatarLoadedCallback = Callback<AvatarImageLoaded_t>.Create(OnAvatarImageLoaded);

            Debug.Log($"[SteamManager] Steam initialised for {LocalDisplayName} ({LocalSteamIdValue}).");
        }

        private void Update()
        {
            if (_initialized)
                SteamAPI.RunCallbacks();
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            if (_initialized)
                SteamAPI.Shutdown();

            Instance = null;
        }

        private void OnApplicationQuit()
        {
            if (_initialized)
            {
                SteamAPI.Shutdown();
                _initialized = false;
            }
        }

        /// <summary>Persona (display) name for any Steam user.</summary>
        public static string GetDisplayName(ulong steamId)
            => Initialized ? SteamFriends.GetFriendPersonaName(new CSteamID(steamId)) : "Player";

        /// <summary>
        /// Asynchronously resolves a user's avatar as a UI <see cref="Sprite"/>.
        /// Invokes <paramref name="onLoaded"/> with null when no avatar is
        /// available (the caller should fall back to a default icon).
        /// </summary>
        public void RequestAvatar(ulong steamId, Action<Sprite> onLoaded)
        {
            if (onLoaded == null)
                return;

            if (!Initialized || steamId == 0UL)
            {
                onLoaded(null);
                return;
            }

            if (_avatarCache.TryGetValue(steamId, out Sprite cached) && cached != null)
            {
                onLoaded(cached);
                return;
            }

            var id = new CSteamID(steamId);
            int handle = SteamFriends.GetLargeFriendAvatar(id);

            if (handle == -1)
            {
                // No avatar set for this user; Steam will not raise a callback.
                onLoaded(null);
                return;
            }

            if (handle == 0)
            {
                // Not cached locally yet — Steam fetches it and raises AvatarImageLoaded_t.
                _avatarPending[steamId] = onLoaded;
                return;
            }

            Sprite sprite = BuildAvatarSprite(handle);
            if (sprite != null)
                _avatarCache[steamId] = sprite;
            onLoaded(sprite);
        }

        private void OnAvatarImageLoaded(AvatarImageLoaded_t cb)
        {
            ulong key = cb.m_steamID.m_SteamID;
            Sprite sprite = BuildAvatarSprite(cb.m_iImage);
            if (sprite != null)
                _avatarCache[key] = sprite;

            if (_avatarPending.TryGetValue(key, out Action<Sprite> pending))
            {
                _avatarPending.Remove(key);
                pending?.Invoke(sprite);
            }
        }

        private static Sprite BuildAvatarSprite(int handle)
        {
            if (handle <= 0)
                return null;

            if (!SteamUtils.GetImageSize(handle, out uint width, out uint height) || width == 0 || height == 0)
                return null;

            int byteCount = (int)(width * height * 4);
            var data = new byte[byteCount];
            if (!SteamUtils.GetImageRGBA(handle, data, byteCount))
                return null;

            // Steam returns rows top-to-bottom; Unity textures expect bottom-to-top.
            FlipVertical(data, (int)width, (int)height);

            var tex = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false, true);
            tex.LoadRawTextureData(data);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void FlipVertical(byte[] rgba, int width, int height)
        {
            int stride = width * 4;
            var row = new byte[stride];
            for (int y = 0; y < height / 2; y++)
            {
                int top = y * stride;
                int bottom = (height - 1 - y) * stride;
                Array.Copy(rgba, top, row, 0, stride);
                Array.Copy(rgba, bottom, rgba, top, stride);
                Array.Copy(row, 0, rgba, bottom, stride);
            }
        }

        [AOT.MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
        private static void OnSteamWarning(int severity, System.Text.StringBuilder debugText)
            => Debug.LogWarning($"[Steam] {debugText}");
    }
}
