using System;
using System.Threading;
using Bun3.Unity.Steam;
using Cysharp.Threading.Tasks;
using Steamworks;

namespace Bun3.Unity.Rooms.Steam
{
    internal sealed class SteamLobbyApi : ISteamLobbyApi, IDisposable
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
        private readonly Callback<LobbyDataUpdate_t> dataUpdated;
        private readonly Callback<LobbyChatUpdate_t> chatUpdated;
        private readonly Callback<LobbyKicked_t> kicked;
        private readonly Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
        private readonly Callback<GameRichPresenceJoinRequested_t> presenceJoinRequested;

        public SteamLobbyApi()
        {
            dataUpdated = Callback<LobbyDataUpdate_t>.Create(data => LobbyDataUpdated?.Invoke(data.m_ulSteamIDLobby, data.m_ulSteamIDMember));
            chatUpdated = Callback<LobbyChatUpdate_t>.Create(data =>
                LobbyChatUpdated?.Invoke(data.m_ulSteamIDLobby, data.m_ulSteamIDUserChanged, data.m_rgfChatMemberStateChange));
            kicked = Callback<LobbyKicked_t>.Create(data => LobbyKicked?.Invoke(data.m_ulSteamIDLobby));
            lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(data => LobbyJoinRequested?.Invoke(data.m_steamIDLobby.m_SteamID));
            presenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(data => RichPresenceJoinRequested?.Invoke(data.m_rgchConnect));
        }

        public bool IsAvailable => SteamRuntime.IsInitialized;
        public ulong LocalUserId => SteamUser.GetSteamID().m_SteamID;
        public uint ServerRealTime => SteamUtils.GetServerRealTime();
        public string CommandLine => Environment.CommandLine;

        public event Action<ulong, ulong> LobbyDataUpdated;
        public event Action<ulong, ulong, uint> LobbyChatUpdated;
        public event Action<ulong> LobbyKicked;
        public event Action<ulong> LobbyJoinRequested;
        public event Action<string> RichPresenceJoinRequested;

        public UniTask<ulong> CreateLobbyAsync(int maxMembers, CancellationToken cancellationToken) =>
            CallAsync<LobbyCreated_t, ulong>(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeInvisible, maxMembers),
                (data, ioFailure) => ioFailure || data.m_eResult != EResult.k_EResultOK ? 0 : data.m_ulSteamIDLobby, cancellationToken);

        public UniTask<int> FindLobbiesAsync(string key, string value, bool requireOpenSlot, CancellationToken cancellationToken)
        {
            SteamMatchmaking.AddRequestLobbyListStringFilter(key, value, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            if (requireOpenSlot) SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(1);
            return CallAsync<LobbyMatchList_t, int>(SteamMatchmaking.RequestLobbyList(),
                (data, ioFailure) => ioFailure ? -1 : (int)data.m_nLobbiesMatching, cancellationToken);
        }

        public ulong GetLobbyByIndex(int index) => SteamMatchmaking.GetLobbyByIndex(index).m_SteamID;

        public UniTask<uint> JoinLobbyAsync(ulong lobby, CancellationToken cancellationToken) =>
            CallAsync<LobbyEnter_t, uint>(SteamMatchmaking.JoinLobby(new CSteamID(lobby)),
                (data, ioFailure) => ioFailure ? (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseError : data.m_EChatRoomEnterResponse,
                cancellationToken);

        public void LeaveLobby(ulong lobby) => SteamMatchmaking.LeaveLobby(new CSteamID(lobby));
        public string GetLobbyData(ulong lobby, string key) => SteamMatchmaking.GetLobbyData(new CSteamID(lobby), key);
        public bool SetLobbyData(ulong lobby, string key, string value) => SteamMatchmaking.SetLobbyData(new CSteamID(lobby), key, value);

        public string GetMemberData(ulong lobby, ulong member, string key) =>
            SteamMatchmaking.GetLobbyMemberData(new CSteamID(lobby), new CSteamID(member), key);

        public void SetMemberData(ulong lobby, string key, string value) => SteamMatchmaking.SetLobbyMemberData(new CSteamID(lobby), key, value);
        public int GetMemberCount(ulong lobby) => SteamMatchmaking.GetNumLobbyMembers(new CSteamID(lobby));
        public int GetMemberLimit(ulong lobby) => SteamMatchmaking.GetLobbyMemberLimit(new CSteamID(lobby));
        public ulong GetMemberByIndex(ulong lobby, int index) => SteamMatchmaking.GetLobbyMemberByIndex(new CSteamID(lobby), index).m_SteamID;
        public ulong GetOwner(ulong lobby) => SteamMatchmaking.GetLobbyOwner(new CSteamID(lobby)).m_SteamID;
        public bool SetOwner(ulong lobby, ulong member) => SteamMatchmaking.SetLobbyOwner(new CSteamID(lobby), new CSteamID(member));
        public bool SetJoinable(ulong lobby, bool joinable) => SteamMatchmaking.SetLobbyJoinable(new CSteamID(lobby), joinable);
        public void SetRichPresence(string key, string value) => SteamFriends.SetRichPresence(key, value ?? string.Empty);
        public void OpenInviteDialog(ulong lobby) => SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobby));

        public void Dispose()
        {
            dataUpdated.Dispose();
            chatUpdated.Dispose();
            kicked.Dispose();
            lobbyJoinRequested.Dispose();
            presenceJoinRequested.Dispose();
        }

        private static async UniTask<TResult> CallAsync<TCallback, TResult>(SteamAPICall_t call, Func<TCallback, bool, TResult> map,
            CancellationToken cancellationToken)
        {
            var source = new UniTaskCompletionSource<TResult>();
            using (var result = CallResult<TCallback>.Create((data, ioFailure) => source.TrySetResult(map(data, ioFailure))))
            {
                result.Set(call);
                try
                {
                    return await source.Task.Timeout(RequestTimeout).AttachExternalCancellation(cancellationToken);
                }
                catch (TimeoutException)
                {
                    throw new RoomException(RoomFailure.ServiceUnavailable);
                }
            }
        }
    }
}
