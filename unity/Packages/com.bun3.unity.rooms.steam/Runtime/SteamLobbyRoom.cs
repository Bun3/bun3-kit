using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Steamworks;

namespace Bun3.Unity.Rooms.Steam
{
    internal sealed class SteamLobbyRoom : IRoom
    {
        private const uint LeftMask =
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeKicked |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeBanned;

        private readonly SteamLobbyBackend backend;
        private readonly ISteamLobbyApi api;
        private readonly List<RoomMember> members = new List<RoomMember>();
        private readonly string connectValue;
        private ulong owner;
        private bool released;

        public SteamLobbyRoom(SteamLobbyBackend backend, ISteamLobbyApi api, ulong lobby, RoomCode code)
        {
            this.backend = backend;
            this.api = api;
            Lobby = lobby;
            Code = code;
            Id = lobby.ToString(CultureInfo.InvariantCulture);
            LocalMemberId = api.LocalUserId.ToString(CultureInfo.InvariantCulture);
            connectValue = string.Concat(SteamLobbyBackend.ConnectCommand, " ", Id);
            api.LobbyDataUpdated += OnLobbyDataUpdated;
            api.LobbyChatUpdated += OnLobbyChatUpdated;
            api.LobbyKicked += OnLobbyKicked;
            Refresh();
            PublishInvite();
        }

        internal ulong Lobby { get; }
        public string Id { get; }
        public RoomCode Code { get; }
        public string Name { get; private set; }
        public IReadOnlyList<RoomMember> Members => members;
        public int MaxMembers => api.GetMemberLimit(Lobby);
        public string LocalMemberId { get; }
        public string OwnerId { get; private set; }
        public bool IsOwner => OwnerId == LocalMemberId;
        public bool IsJoinable { get; private set; } = true;
        public bool IsPublic => GetData(SteamLobbyBackend.PublicKey) == "1";
        public event Action MembersChanged;
        public event Action OwnerChanged;
        public event Action DataChanged;
        public event Action Closed;

        public string GetData(string key)
        {
            string value = api.GetLobbyData(Lobby, key);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        public UniTask SetDataAsync(string key, string value)
        {
            RequireOwner();
            if (!api.SetLobbyData(Lobby, key, value ?? string.Empty)) throw new RoomException(RoomFailure.ServiceUnavailable);
            return UniTask.CompletedTask;
        }

        // Steam has no getter for the joinable flag, so the owner mirrors it in lobby data for every member.
        public UniTask SetJoinableAsync(bool joinable)
        {
            RequireOwner();
            if (!api.SetJoinable(Lobby, joinable) || !api.SetLobbyData(Lobby, SteamLobbyBackend.OpenKey, joinable ? "1" : "0"))
                throw new RoomException(RoomFailure.ServiceUnavailable);
            if (IsJoinable != joinable)
            {
                IsJoinable = joinable;
                PublishInvite();
            }
            return UniTask.CompletedTask;
        }

        public UniTask SetPublicAsync(bool isPublic)
        {
            RequireOwner();
            if (!api.SetLobbyData(Lobby, SteamLobbyBackend.PublicKey, isPublic ? "1" : "0")) throw new RoomException(RoomFailure.ServiceUnavailable);
            return UniTask.CompletedTask;
        }

        public UniTask TransferOwnershipAsync(string memberId)
        {
            RequireOwner();
            if (!ulong.TryParse(memberId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong member) || !api.SetOwner(Lobby, member))
                throw new RoomException(RoomFailure.ServiceUnavailable);
            return UniTask.CompletedTask;
        }

        public UniTask LeaveAsync()
        {
            if (Release()) api.LeaveLobby(Lobby);
            return UniTask.CompletedTask;
        }

        // member == lobby means lobby data changed; otherwise it is that member's data.
        private void OnLobbyDataUpdated(ulong lobby, ulong member)
        {
            if (lobby != Lobby) return;
            Refresh();
            if (member == lobby) DataChanged?.Invoke();
        }

        private void OnLobbyChatUpdated(ulong lobby, ulong user, uint change)
        {
            if (lobby != Lobby) return;
            if (user == api.LocalUserId && (change & LeftMask) != 0) RemoveLocal();
            else Refresh();
        }

        private void OnLobbyKicked(ulong lobby)
        {
            if (lobby == Lobby) RemoveLocal();
        }

        private void RemoveLocal()
        {
            if (!Release()) return;
            api.LeaveLobby(Lobby);
            Closed?.Invoke();
        }

        private bool Release()
        {
            if (released) return false;
            released = true;
            api.LobbyDataUpdated -= OnLobbyDataUpdated;
            api.LobbyChatUpdated -= OnLobbyChatUpdated;
            api.LobbyKicked -= OnLobbyKicked;
            api.SetRichPresence(SteamLobbyBackend.ConnectKey, null);
            backend.Release(this);
            return true;
        }

        private void Refresh()
        {
            Name = GetData(SteamLobbyBackend.NameKey);
            bool joinable = GetData(SteamLobbyBackend.OpenKey) != "0";
            if (joinable != IsJoinable)
            {
                IsJoinable = joinable;
                PublishInvite();
            }
            bool membersChanged = RefreshMembers();
            ulong latestOwner = api.GetOwner(Lobby);
            bool ownerChanged = latestOwner != owner || OwnerId == null;
            if (ownerChanged)
            {
                owner = latestOwner;
                OwnerId = latestOwner.ToString(CultureInfo.InvariantCulture);
            }
            if ((membersChanged || ownerChanged) && IsOwner) PublishMemberCount();
            if (membersChanged) MembersChanged?.Invoke();
            if (ownerChanged) OwnerChanged?.Invoke();
        }

        private bool RefreshMembers()
        {
            int count = api.GetMemberCount(Lobby);
            bool changed = count != members.Count;
            for (int i = 0; i < count; i++)
            {
                ulong steamId = api.GetMemberByIndex(Lobby, i);
                long joinedAt = long.TryParse(api.GetMemberData(Lobby, steamId, SteamLobbyBackend.JoinedKey),
                    NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : long.MaxValue;
                string id = steamId.ToString(CultureInfo.InvariantCulture);
                if (i < members.Count)
                {
                    if (members[i].Id == id && members[i].JoinedAt == joinedAt) continue;
                    members[i] = new RoomMember(id, joinedAt);
                    changed = true;
                }
                else
                {
                    members.Add(new RoomMember(id, joinedAt));
                }
            }
            if (members.Count > count) members.RemoveRange(count, members.Count - count);
            return changed;
        }

        // Friends see "Join Game" only while the room is open.
        private void PublishInvite() => api.SetRichPresence(SteamLobbyBackend.ConnectKey, IsJoinable ? connectValue : null);

        // Lobby lists read the count from lobby data: the docs do not promise GetNumLobbyMembers for lobbies the client is not in.
        private void PublishMemberCount()
        {
            string count = members.Count.ToString(CultureInfo.InvariantCulture);
            if (GetData(SteamLobbyBackend.MembersKey) != count) api.SetLobbyData(Lobby, SteamLobbyBackend.MembersKey, count);
        }

        private void RequireOwner()
        {
            if (!IsOwner) throw new InvalidOperationException("Only the lobby owner can change the room.");
        }
    }
}
