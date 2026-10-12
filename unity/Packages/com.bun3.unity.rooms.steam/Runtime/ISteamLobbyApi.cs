using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Rooms.Steam
{
    // Steam lobby, friends and utility calls behind a seam so backend logic runs without a Steam client in tests.
    internal interface ISteamLobbyApi
    {
        bool IsAvailable { get; }
        ulong LocalUserId { get; }
        uint ServerRealTime { get; }
        string CommandLine { get; }
        UniTask<ulong> CreateLobbyAsync(int maxMembers, CancellationToken cancellationToken);
        // Returns the match count (at most one), or a negative number when the request failed.
        UniTask<int> FindLobbiesAsync(string key, string value, bool requireOpenSlot, CancellationToken cancellationToken);
        ulong GetLobbyByIndex(int index);
        UniTask<uint> JoinLobbyAsync(ulong lobby, CancellationToken cancellationToken);
        void LeaveLobby(ulong lobby);
        string GetLobbyData(ulong lobby, string key);
        bool SetLobbyData(ulong lobby, string key, string value);
        string GetMemberData(ulong lobby, ulong member, string key);
        void SetMemberData(ulong lobby, string key, string value);
        int GetMemberCount(ulong lobby);
        int GetMemberLimit(ulong lobby);
        ulong GetMemberByIndex(ulong lobby, int index);
        ulong GetOwner(ulong lobby);
        bool SetOwner(ulong lobby, ulong member);
        bool SetJoinable(ulong lobby, bool joinable);
        // A null or empty value removes the key.
        void SetRichPresence(string key, string value);
        void OpenInviteDialog(ulong lobby);
        event Action<ulong, ulong> LobbyDataUpdated;
        event Action<ulong, ulong, uint> LobbyChatUpdated;
        event Action<ulong> LobbyKicked;
        // Raised for every successfully entered lobby, including ones whose request was abandoned.
        event Action<ulong> LobbyEntered;
        event Action<ulong> LobbyJoinRequested;
        event Action<string> RichPresenceJoinRequested;
    }
}
