using System;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Steamworks;

namespace Bun3.Unity.Rooms.Steam
{
    /// <summary>
    /// Rooms as invisible Steam lobbies. Room codes are searched worldwide through lobby data; friends join
    /// through the overlay invite or "Join Game", which arrive as <see cref="JoinRequested"/>. Construct after
    /// <c>SteamRuntime.Initialize</c>. While Steam is not initialized every call fails with
    /// <see cref="RoomFailure.ServiceUnavailable"/>. Main thread only.
    /// </summary>
    public sealed class SteamLobbyBackend : IRoomBackend, IDisposable
    {
        /// <summary>Lobby data key holding the room code.</summary>
        public const string CodeKey = "bun3.code";

        /// <summary>Lobby data key holding the room version string.</summary>
        public const string VersionKey = "bun3.ver";

        /// <summary>Lobby data key holding the room name.</summary>
        public const string NameKey = "bun3.name";

        /// <summary>Lobby data key the owner sets to <c>"0"</c> while the room is closed to new members.</summary>
        public const string OpenKey = "bun3.open";

        /// <summary>Member data key holding each member's join time (Steam server time).</summary>
        public const string JoinedKey = "bun3.joined";

        internal const string ConnectKey = "connect";
        internal const string ConnectCommand = "+connect_lobby";
        private const int MaxCodeAttempts = 5;
        private readonly ISteamLobbyApi api;
        private SteamLobbyRoom current;
        private bool inFlight;

        /// <summary>Creates a backend on the initialized Steam client.</summary>
        public SteamLobbyBackend() : this(new SteamLobbyApi())
        {
        }

        internal SteamLobbyBackend(ISteamLobbyApi api)
        {
            this.api = api;
            api.LobbyJoinRequested += OnLobbyJoinRequested;
            api.RichPresenceJoinRequested += OnRichPresenceJoinRequested;
            api.LobbyEntered += OnLobbyEntered;
            // Raised next frame so the creator can subscribe first.
            if (TryParseConnect(api.CommandLine, out string launchLobby))
                UniTask.Post(() => JoinRequested?.Invoke(launchLobby));
        }

        /// <inheritdoc/>
        public event Action<string> JoinRequested;

        /// <summary>Creates an invisible lobby with an unused code. Gives up after five code collisions.</summary>
        public async UniTask<IRoom> CreateAsync(RoomCreateOptions options, CancellationToken cancellationToken = default)
        {
            EnsureReady();
            inFlight = true;
            try
            {
                RoomCode code = default;
                bool unique = false;
                for (int attempt = 0; attempt < MaxCodeAttempts && !unique; attempt++)
                {
                    code = RoomCode.Generate();
                    int found = await api.FindLobbiesAsync(CodeKey, code.ToString(), false, cancellationToken);
                    if (found < 0) throw new RoomException(RoomFailure.ServiceUnavailable);
                    unique = found == 0;
                }
                if (!unique) throw new RoomException(RoomFailure.ServiceUnavailable);
                ulong lobby = await api.CreateLobbyAsync(options.MaxMembers, cancellationToken);
                if (lobby == 0) throw new RoomException(RoomFailure.ServiceUnavailable);
                api.SetLobbyData(lobby, CodeKey, code.ToString());
                api.SetLobbyData(lobby, VersionKey, options.Version ?? string.Empty);
                api.SetLobbyData(lobby, NameKey, options.Name ?? string.Empty);
                return Enter(lobby, code);
            }
            finally
            {
                inFlight = false;
            }
        }

        /// <inheritdoc/>
        public async UniTask<IRoom> JoinByCodeAsync(RoomCode code, RoomJoinOptions options, CancellationToken cancellationToken = default)
        {
            EnsureReady();
            inFlight = true;
            try
            {
                int found = await api.FindLobbiesAsync(CodeKey, code.ToString(), true, cancellationToken);
                if (found < 0) throw new RoomException(RoomFailure.ServiceUnavailable);
                if (found == 0) throw new RoomException(RoomFailure.NotFound);
                return await JoinAsync(api.GetLobbyByIndex(0), options, cancellationToken);
            }
            finally
            {
                inFlight = false;
            }
        }

        /// <summary>Joins by decimal lobby SteamID, as delivered by <see cref="JoinRequested"/>.</summary>
        public async UniTask<IRoom> JoinByIdAsync(string roomId, RoomJoinOptions options, CancellationToken cancellationToken = default)
        {
            EnsureReady();
            if (!ulong.TryParse(roomId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong lobby) || lobby == 0)
                throw new RoomException(RoomFailure.NotFound);
            inFlight = true;
            try
            {
                return await JoinAsync(lobby, options, cancellationToken);
            }
            finally
            {
                inFlight = false;
            }
        }

        /// <summary>Opens the Steam overlay invite dialog for the current room.</summary>
        public bool TryOpenInviteOverlay(IRoom room)
        {
            if (!(room is SteamLobbyRoom lobby) || lobby != current || !api.IsAvailable) return false;
            api.OpenInviteDialog(lobby.Lobby);
            return true;
        }

        /// <summary>
        /// Stops listening for Steam callbacks. It does not leave the current room or clear rich presence (Steam clears
        /// both when the API shuts down); call <c>LeaveAsync</c> on the room first.
        /// </summary>
        public void Dispose()
        {
            api.LobbyJoinRequested -= OnLobbyJoinRequested;
            api.RichPresenceJoinRequested -= OnRichPresenceJoinRequested;
            api.LobbyEntered -= OnLobbyEntered;
            (api as IDisposable)?.Dispose();
        }

        internal static bool TryParseConnect(string text, out string lobbyId)
        {
            lobbyId = null;
            if (string.IsNullOrEmpty(text)) return false;
            int start = text.IndexOf(ConnectCommand, StringComparison.Ordinal);
            if (start < 0) return false;
            start += ConnectCommand.Length;
            while (start < text.Length && text[start] == ' ') start++;
            int end = start;
            while (end < text.Length && text[end] >= '0' && text[end] <= '9') end++;
            if (end == start ||
                !ulong.TryParse(text.Substring(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out ulong lobby) ||
                lobby == 0)
                return false;
            lobbyId = lobby.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        internal void Release(SteamLobbyRoom room)
        {
            if (current == room) current = null;
        }

        private async UniTask<IRoom> JoinAsync(ulong lobby, RoomJoinOptions options, CancellationToken cancellationToken)
        {
            uint response = await api.JoinLobbyAsync(lobby, cancellationToken);
            if (response != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                throw new RoomException(
                    response == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseFull ? RoomFailure.Full :
                    response == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseError ? RoomFailure.ServiceUnavailable :
                    RoomFailure.NotFound);
            if (!string.Equals(api.GetLobbyData(lobby, VersionKey), options.Version ?? string.Empty, StringComparison.Ordinal))
            {
                api.LeaveLobby(lobby);
                throw new RoomException(RoomFailure.VersionMismatch);
            }
            if (!RoomCode.TryNormalize(api.GetLobbyData(lobby, CodeKey), out RoomCode code))
            {
                api.LeaveLobby(lobby);
                throw new RoomException(RoomFailure.NotFound);
            }
            return Enter(lobby, code);
        }

        private IRoom Enter(ulong lobby, RoomCode code)
        {
            api.SetMemberData(lobby, JoinedKey, api.ServerRealTime.ToString(CultureInfo.InvariantCulture));
            current = new SteamLobbyRoom(this, api, lobby, code);
            return current;
        }

        private void EnsureReady()
        {
            if (!api.IsAvailable) throw new RoomException(RoomFailure.ServiceUnavailable);
            if (current != null) throw new InvalidOperationException("Leave the current room first.");
            if (inFlight) throw new InvalidOperationException("Another room operation is still running.");
        }

        // Steam enters a lobby even after its request was abandoned; leave it so no orphan lobby or ghost member remains.
        // Ceiling: a late enter that arrives while a NEW operation is in flight is not left.
        private void OnLobbyEntered(ulong lobby)
        {
            if (!inFlight && (current == null || current.Lobby != lobby)) api.LeaveLobby(lobby);
        }

        private void OnLobbyJoinRequested(ulong lobby) => JoinRequested?.Invoke(lobby.ToString(CultureInfo.InvariantCulture));

        private void OnRichPresenceJoinRequested(string connect)
        {
            if (TryParseConnect(connect, out string lobby)) JoinRequested?.Invoke(lobby);
        }
    }
}
