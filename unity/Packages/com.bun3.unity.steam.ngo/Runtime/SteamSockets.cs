using System;
using System.Runtime.InteropServices;
using Steamworks;

namespace Bun3.Unity.Steam.Ngo
{
    internal sealed class SteamSockets : ISteamSockets, IDisposable
    {
        private const int BatchSize = 64;
        private static readonly int DataOffset = (int)Marshal.OffsetOf<SteamNetworkingMessage_t>(nameof(SteamNetworkingMessage_t.m_pData));
        private static readonly int SizeOffset = (int)Marshal.OffsetOf<SteamNetworkingMessage_t>(nameof(SteamNetworkingMessage_t.m_cbSize));
        private static readonly int ConnectionOffset = (int)Marshal.OffsetOf<SteamNetworkingMessage_t>(nameof(SteamNetworkingMessage_t.m_conn));
        private readonly IntPtr[] batch = new IntPtr[BatchSize];
        private int batchCount;
        private int batchIndex;
        private Callback<SteamNetConnectionStatusChangedCallback_t> statusCallback;

        public bool IsAvailable => SteamRuntime.IsInitialized;
        public event Action<SteamConnectionStatus> StatusChanged;

        public void InitRelayNetworkAccess()
        {
            statusCallback ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
            SteamNetworkingUtils.InitRelayNetworkAccess();
        }

        public uint CreateListenSocket(int virtualPort) =>
            SteamNetworkingSockets.CreateListenSocketP2P(virtualPort, 0, null).m_HSteamListenSocket;

        public void CloseListenSocket(uint socket) => SteamNetworkingSockets.CloseListenSocket(new HSteamListenSocket(socket));

        public uint CreatePollGroup() => SteamNetworkingSockets.CreatePollGroup().m_HSteamNetPollGroup;

        public void DestroyPollGroup(uint group) => SteamNetworkingSockets.DestroyPollGroup(new HSteamNetPollGroup(group));

        public uint Connect(ulong steamId, int virtualPort)
        {
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(steamId);
            return SteamNetworkingSockets.ConnectP2P(ref identity, virtualPort, 0, null).m_HSteamNetConnection;
        }

        public bool Accept(uint connection) =>
            SteamNetworkingSockets.AcceptConnection(new HSteamNetConnection(connection)) == EResult.k_EResultOK;

        public void SetPollGroup(uint connection, uint group) =>
            SteamNetworkingSockets.SetConnectionPollGroup(new HSteamNetConnection(connection), new HSteamNetPollGroup(group));

        public void Close(uint connection, bool linger) =>
            SteamNetworkingSockets.CloseConnection(new HSteamNetConnection(connection), 0, null, linger);

        public ulong GetRemoteSteamId(uint connection) =>
            SteamNetworkingSockets.GetConnectionInfo(new HSteamNetConnection(connection), out SteamNetConnectionInfo_t info)
                ? info.m_identityRemote.GetSteamID64()
                : 0;

        public bool Send(uint connection, ArraySegment<byte> payload, int flags)
        {
            var pinned = GCHandle.Alloc(payload.Array, GCHandleType.Pinned);
            try
            {
                return SteamNetworkingSockets.SendMessageToConnection(new HSteamNetConnection(connection),
                    pinned.AddrOfPinnedObject() + payload.Offset, (uint)payload.Count, flags, out _) == EResult.k_EResultOK;
            }
            finally
            {
                pinned.Free();
            }
        }

        public bool TryReceive(uint source, bool pollGroup, ref byte[] buffer, out int size, out uint connection)
        {
            if (batchIndex >= batchCount)
            {
                batchIndex = 0;
                batchCount = pollGroup
                    ? SteamNetworkingSockets.ReceiveMessagesOnPollGroup(new HSteamNetPollGroup(source), batch, BatchSize)
                    : SteamNetworkingSockets.ReceiveMessagesOnConnection(new HSteamNetConnection(source), batch, BatchSize);
                if (batchCount <= 0)
                {
                    batchCount = 0;
                    size = 0;
                    connection = 0;
                    return false;
                }
            }
            IntPtr message = batch[batchIndex++];
            try
            {
                size = Marshal.ReadInt32(message, SizeOffset);
                connection = (uint)Marshal.ReadInt32(message, ConnectionOffset);
                if (buffer.Length < size) buffer = new byte[Math.Max(size, buffer.Length * 2)];
                Marshal.Copy(Marshal.ReadIntPtr(message, DataOffset), buffer, 0, size);
            }
            finally
            {
                SteamNetworkingMessage_t.Release(message);
            }
            return true;
        }

        public void DiscardReceived()
        {
            while (batchIndex < batchCount) SteamNetworkingMessage_t.Release(batch[batchIndex++]);
            batchIndex = 0;
            batchCount = 0;
        }

        public int GetPing(uint connection)
        {
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(new HSteamNetConnection(connection), ref status, 0, ref lanes) == EResult.k_EResultOK
                ? status.m_nPing
                : 0;
        }

        public void Dispose()
        {
            DiscardReceived();
            statusCallback?.Dispose();
            statusCallback = null;
        }

        private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t data) =>
            StatusChanged?.Invoke(new SteamConnectionStatus(data.m_hConn.m_HSteamNetConnection,
                data.m_info.m_hListenSocket.m_HSteamListenSocket, data.m_info.m_identityRemote.GetSteamID64(), data.m_info.m_eState));
    }
}
