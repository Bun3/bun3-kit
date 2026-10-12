using System;
using Steamworks;

namespace Bun3.Unity.Steam.Ngo
{
    // Steam networking behind a seam so transport logic runs without a Steam client in tests.
    internal interface ISteamSockets
    {
        bool IsAvailable { get; }
        event Action<SteamConnectionStatus> StatusChanged;
        void InitRelayNetworkAccess();
        uint CreateListenSocket(int virtualPort);
        void CloseListenSocket(uint socket);
        uint CreatePollGroup();
        void DestroyPollGroup(uint group);
        uint Connect(ulong steamId, int virtualPort);
        bool Accept(uint connection);
        void SetPollGroup(uint connection, uint group);
        void Close(uint connection, bool linger);
        ulong GetRemoteSteamId(uint connection);
        bool Send(uint connection, ArraySegment<byte> payload, int flags);
        // Copies the next message from a poll group or a connection into buffer, growing it when needed.
        bool TryReceive(uint source, bool pollGroup, ref byte[] buffer, out int size, out uint connection);
        void DiscardReceived();
        int GetPing(uint connection);
    }

    internal readonly struct SteamConnectionStatus
    {
        public SteamConnectionStatus(uint connection, uint listenSocket, ulong remoteSteamId, ESteamNetworkingConnectionState state)
        {
            Connection = connection;
            ListenSocket = listenSocket;
            RemoteSteamId = remoteSteamId;
            State = state;
        }

        public uint Connection { get; }
        public uint ListenSocket { get; }
        public ulong RemoteSteamId { get; }
        public ESteamNetworkingConnectionState State { get; }
    }
}
