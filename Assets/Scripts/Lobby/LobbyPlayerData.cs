using System;

namespace FightOrFlight.Lobby
{
    /// <summary>
    /// Network-replicated snapshot of a single player in the lobby.
    ///
    /// The avatar is intentionally NOT stored here — avatars are bitmaps that
    /// would be expensive to replicate. Each client fetches the avatar locally
    /// from Steam using <see cref="SteamId"/>, so only this small value type is
    /// synchronised across the network.
    /// </summary>
    [Serializable]
    public struct LobbyPlayerData : IEquatable<LobbyPlayerData>
    {
        /// <summary>FishNet connection id — the stable network identity of the player.</summary>
        public int ClientId;

        /// <summary>Steam id used to resolve the nickname and avatar (0 when Steam is off).</summary>
        public ulong SteamId;

        /// <summary>Display name (Steam persona, or a fallback).</summary>
        public string DisplayName;

        /// <summary>True for the player that owns the lobby.</summary>
        public bool IsHost;

        /// <summary>Ready-check state, ready to drive a future "all ready to start" rule.</summary>
        public bool IsReady;

        public bool Equals(LobbyPlayerData other)
        {
            return ClientId == other.ClientId
                   && SteamId == other.SteamId
                   && IsHost == other.IsHost
                   && IsReady == other.IsReady
                   && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is LobbyPlayerData other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ClientId, SteamId, DisplayName, IsHost, IsReady);
    }
}
