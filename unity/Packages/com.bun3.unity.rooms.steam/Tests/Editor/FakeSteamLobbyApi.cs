using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Steamworks;

namespace Bun3.Unity.Rooms.Steam.Tests
{
    internal sealed class FakeSteamLobbyApi : ISteamLobbyApi
    {
        internal sealed class Lobby
        {
            public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
            public readonly Dictionary<(ulong, string), string> MemberData = new Dictionary<(ulong, string), string>();
            public readonly List<ulong> Members = new List<ulong>();
            public ulong Owner;
            public int MaxMembers;
            public bool Joinable = true;
        }

        private const uint Success = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
        public readonly Dictionary<ulong, Lobby> Lobbies = new Dictionary<ulong, Lobby>();
        public readonly Dictionary<string, string> RichPresence = new Dictionary<string, string>();
        public readonly List<ulong> Left = new List<ulong>();
        private readonly List<ulong> results = new List<ulong>();
        private ulong nextLobby = 109775240000000001;
        public int CollisionsRemaining;
        public int Searches;
        public bool LastSearchRequiredSlot;
        public uint JoinResponse = Success;
        public ulong InviteDialogLobby;
        public bool JoinTimesOut;
        public UniTaskCompletionSource<uint> JoinGate;

        public bool IsAvailable { get; set; } = true;
        public ulong LocalUserId { get; set; }
        public uint ServerRealTime { get; set; } = 1000;
        public string CommandLine { get; set; } = string.Empty;

        public event Action<ulong, ulong> LobbyDataUpdated;
        public event Action<ulong, ulong, uint> LobbyChatUpdated;
        public event Action<ulong> LobbyKicked;
        public event Action<ulong> LobbyEntered;
        public event Action<ulong> LobbyJoinRequested;
        public event Action<string> RichPresenceJoinRequested;

        public ulong AddLobby(ulong owner, string code, string version, string ownerJoined, int maxMembers = 8)
        {
            ulong id = nextLobby++;
            var lobby = new Lobby { Owner = owner, MaxMembers = maxMembers };
            lobby.Members.Add(owner);
            lobby.Data[SteamLobbyBackend.CodeKey] = code;
            lobby.Data[SteamLobbyBackend.VersionKey] = version;
            lobby.Data[SteamLobbyBackend.NameKey] = "Friend";
            lobby.MemberData[(owner, SteamLobbyBackend.JoinedKey)] = ownerJoined;
            Lobbies[id] = lobby;
            return id;
        }

        public UniTask<ulong> CreateLobbyAsync(int maxMembers, CancellationToken cancellationToken)
        {
            ulong id = nextLobby++;
            var lobby = new Lobby { Owner = LocalUserId, MaxMembers = maxMembers };
            lobby.Members.Add(LocalUserId);
            Lobbies[id] = lobby;
            LobbyEntered?.Invoke(id);
            return UniTask.FromResult(id);
        }

        public UniTask<int> FindLobbiesAsync(string key, string value, bool requireOpenSlot, CancellationToken cancellationToken)
        {
            Searches++;
            LastSearchRequiredSlot = requireOpenSlot;
            results.Clear();
            if (CollisionsRemaining > 0)
            {
                CollisionsRemaining--;
                results.Add(1);
                return UniTask.FromResult(1);
            }
            foreach (var pair in Lobbies)
            {
                var lobby = pair.Value;
                if (lobby.Joinable && lobby.Data.TryGetValue(key, out string found) && found == value &&
                    (!requireOpenSlot || lobby.Members.Count < lobby.MaxMembers))
                {
                    results.Add(pair.Key);
                    break;
                }
            }
            return UniTask.FromResult(results.Count);
        }

        public ulong GetLobbyByIndex(int index) => results[index];

        public UniTask<uint> JoinLobbyAsync(ulong lobby, CancellationToken cancellationToken)
        {
            if (JoinTimesOut) throw new RoomException(RoomFailure.ServiceUnavailable);
            if (JoinGate != null) return JoinGate.Task;
            if (JoinResponse != Success) return UniTask.FromResult(JoinResponse);
            if (!Lobbies.TryGetValue(lobby, out var entry))
                return UniTask.FromResult((uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist);
            entry.Members.Add(LocalUserId);
            LobbyEntered?.Invoke(lobby);
            return UniTask.FromResult(Success);
        }

        public void LeaveLobby(ulong lobby)
        {
            Left.Add(lobby);
            if (Lobbies.TryGetValue(lobby, out var entry)) entry.Members.Remove(LocalUserId);
        }

        public string GetLobbyData(ulong lobby, string key) =>
            Lobbies.TryGetValue(lobby, out var entry) && entry.Data.TryGetValue(key, out string value) ? value : string.Empty;

        public bool SetLobbyData(ulong lobby, string key, string value)
        {
            if (!Lobbies.TryGetValue(lobby, out var entry) || entry.Owner != LocalUserId) return false;
            entry.Data[key] = value;
            return true;
        }

        public string GetMemberData(ulong lobby, ulong member, string key) =>
            Lobbies.TryGetValue(lobby, out var entry) && entry.MemberData.TryGetValue((member, key), out string value) ? value : string.Empty;

        public void SetMemberData(ulong lobby, string key, string value) => Lobbies[lobby].MemberData[(LocalUserId, key)] = value;
        public int GetMemberCount(ulong lobby) => Lobbies[lobby].Members.Count;
        public int GetMemberLimit(ulong lobby) => Lobbies[lobby].MaxMembers;
        public ulong GetMemberByIndex(ulong lobby, int index) => Lobbies[lobby].Members[index];
        public ulong GetOwner(ulong lobby) => Lobbies.TryGetValue(lobby, out var entry) ? entry.Owner : 0;

        public bool SetOwner(ulong lobby, ulong member)
        {
            var entry = Lobbies[lobby];
            if (entry.Owner != LocalUserId || !entry.Members.Contains(member)) return false;
            entry.Owner = member;
            return true;
        }

        public bool SetJoinable(ulong lobby, bool joinable)
        {
            var entry = Lobbies[lobby];
            if (entry.Owner != LocalUserId) return false;
            entry.Joinable = joinable;
            return true;
        }

        public void SetRichPresence(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) RichPresence.Remove(key);
            else RichPresence[key] = value;
        }

        public void OpenInviteDialog(ulong lobby) => InviteDialogLobby = lobby;

        public void RaiseDataUpdated(ulong lobby, ulong member) => LobbyDataUpdated?.Invoke(lobby, member);
        public void RaiseChatUpdated(ulong lobby, ulong user, uint change) => LobbyChatUpdated?.Invoke(lobby, user, change);
        public void RaiseLobbyEntered(ulong lobby) => LobbyEntered?.Invoke(lobby);
        public void RaiseKicked(ulong lobby) => LobbyKicked?.Invoke(lobby);
        public void RaiseLobbyJoinRequested(ulong lobby) => LobbyJoinRequested?.Invoke(lobby);
        public void RaiseRichPresenceJoinRequested(string connect) => RichPresenceJoinRequested?.Invoke(connect);
    }
}
