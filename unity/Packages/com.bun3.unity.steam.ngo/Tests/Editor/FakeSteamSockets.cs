using System;
using System.Collections.Generic;
using Steamworks;

namespace Bun3.Unity.Steam.Ngo.Tests
{
    internal sealed class FakeSteamSockets : ISteamSockets
    {
        public readonly List<uint> Accepted = new List<uint>();
        public readonly List<uint> Closed = new List<uint>();
        public readonly Dictionary<uint, uint> Groups = new Dictionary<uint, uint>();
        public readonly Dictionary<uint, ulong> RemoteIds = new Dictionary<uint, ulong>();
        public readonly Queue<(uint Connection, byte[] Data)> Inbox = new Queue<(uint Connection, byte[] Data)>();
        public readonly List<(uint Connection, int Flags, byte[] Data)> Sent = new List<(uint Connection, int Flags, byte[] Data)>();
        public uint ListenSocket = 10;
        public uint PollGroup = 20;
        public uint OutgoingConnection = 30;
        public int RelayInits;
        public bool ListenSocketClosed;
        public ulong ConnectedTo;

        public bool IsAvailable { get; set; } = true;
        public event Action<SteamConnectionStatus> StatusChanged;

        public void InitRelayNetworkAccess() => RelayInits++;
        public uint CreateListenSocket(int virtualPort) => ListenSocket;
        public void CloseListenSocket(uint socket) => ListenSocketClosed = true;
        public uint CreatePollGroup() => PollGroup;
        public void DestroyPollGroup(uint group) { }

        public uint Connect(ulong steamId, int virtualPort)
        {
            ConnectedTo = steamId;
            return OutgoingConnection;
        }

        public bool Accept(uint connection)
        {
            Accepted.Add(connection);
            return true;
        }

        public void SetPollGroup(uint connection, uint group) => Groups[connection] = group;
        public void Close(uint connection, bool linger) => Closed.Add(connection);
        public ulong GetRemoteSteamId(uint connection) => RemoteIds.TryGetValue(connection, out ulong id) ? id : 0;

        public bool Send(uint connection, ArraySegment<byte> payload, int flags)
        {
            Sent.Add((connection, flags, payload.ToArray()));
            return true;
        }

        public bool TryReceive(uint source, bool pollGroup, ref byte[] buffer, out int size, out uint connection)
        {
            size = 0;
            connection = 0;
            if (Inbox.Count == 0) return false;
            var (from, data) = Inbox.Dequeue();
            if (buffer.Length < data.Length) buffer = new byte[data.Length];
            Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
            size = data.Length;
            connection = from;
            return true;
        }

        public void DiscardReceived() => Inbox.Clear();
        public int GetPing(uint connection) => 42;

        public void Raise(uint connection, ulong remote, ESteamNetworkingConnectionState state, uint listenSocket) =>
            StatusChanged?.Invoke(new SteamConnectionStatus(connection, listenSocket, remote, state));
    }
}
