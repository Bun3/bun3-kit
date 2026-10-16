using System;
using System.Collections.Generic;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Bun3.Unity.Steam.Ngo
{
    /// <summary>
    /// NGO transport over Steam Datagram Relay peer-to-peer sockets. The host listens on <see cref="VirtualPort"/>;
    /// clients connect to <see cref="TargetSteamId"/>. Requires <see cref="SteamRuntime.IsInitialized"/>.
    /// Receiving reuses one buffer and allocates nothing per frame. Reliable deliveries map to Steam reliable,
    /// unreliable ones to Steam unreliable, both without Nagle delay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamSocketsTransport : NetworkTransport
    {
        private const ulong HostTransportId = 0;
        private const double RttRefreshSeconds = 1;
        private readonly Queue<PendingEvent> events = new Queue<PendingEvent>();
        private readonly Dictionary<ulong, ulong> steamIds = new Dictionary<ulong, ulong>();
        private readonly Dictionary<uint, (double At, ulong Rtt)> rtts = new Dictionary<uint, (double At, ulong Rtt)>();
        private byte[] buffer = new byte[1200];
        private ISteamSockets sockets;
        private uint listenSocket;
        private uint pollGroup;
        private uint hostConnection;
        private uint pendingConnection;
        private int pendingSize;
        private bool hosting;
        private bool joined;

        /// <summary>SteamID of the host a client connects to.</summary>
        public ulong TargetSteamId { get; set; }

        /// <summary>Steam virtual port shared by host and clients.</summary>
        public int VirtualPort { get; set; }

        /// <summary>Decides whether the host accepts a connecting SteamID. Null accepts everyone.</summary>
        public Func<ulong, bool> AcceptFilter { get; set; }

        /// <summary>A client's transport id for its host.</summary>
        public override ulong ServerClientId => HostTransportId;

        internal ISteamSockets Sockets
        {
            get => sockets ??= new SteamSockets();
            set => sockets = value;
        }

        internal Func<double> Clock { get; set; } = () => Time.realtimeSinceStartupAsDouble;

        /// <summary>SteamID behind a transport id: a host's clients, or the host from a client.</summary>
        public bool TryGetSteamId(ulong transportId, out ulong steamId) => steamIds.TryGetValue(transportId, out steamId);

        /// <inheritdoc/>
        public override void Initialize(NetworkManager networkManager = null)
        {
            if (networkManager != null)
                networkManager.MaximumFragmentedMessageSize = Math.Min(networkManager.MaximumFragmentedMessageSize,
                    Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend);
        }

        /// <inheritdoc/>
        public override bool StartServer()
        {
            if (hosting || joined || !Sockets.IsAvailable) return false;
            Sockets.StatusChanged += OnStatusChanged;
            Sockets.InitRelayNetworkAccess();
            hosting = true;
            listenSocket = Sockets.CreateListenSocket(VirtualPort);
            pollGroup = Sockets.CreatePollGroup();
            if (listenSocket != 0 && pollGroup != 0) return true;
            Shutdown();
            return false;
        }

        /// <inheritdoc/>
        public override bool StartClient()
        {
            if (hosting || joined || TargetSteamId == 0 || !Sockets.IsAvailable) return false;
            Sockets.StatusChanged += OnStatusChanged;
            Sockets.InitRelayNetworkAccess();
            joined = true;
            hostConnection = Sockets.Connect(TargetSteamId, VirtualPort);
            if (hostConnection == 0)
            {
                Shutdown();
                return false;
            }
            steamIds[HostTransportId] = TargetSteamId;
            return true;
        }

        /// <inheritdoc/>
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            uint connection = joined ? hostConnection : (uint)clientId;
            if (connection == 0) return;
            var result = Sockets.Send(connection, payload, SendFlags(networkDelivery));
            if (result == EResult.k_EResultOK || result == EResult.k_EResultNoConnection || result == EResult.k_EResultInvalidState) return;
            if (networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced) return;
            // A dropped reliable message would desync NGO silently, so the peer is dropped instead.
            Debug.LogError($"Steam rejected a reliable send ({result}); closing the connection.");
            ulong transportId = joined ? HostTransportId : clientId;
            Sockets.Close(connection, false);
            rtts.Remove(connection);
            if (joined) hostConnection = 0;
            else if (!steamIds.Remove(clientId)) return;
            SetDisconnectEvent(DisconnectEvents.ProtocolTimeout);
            events.Enqueue(new PendingEvent(NetworkEvent.Disconnect, transportId));
        }

        /// <inheritdoc/>
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            clientId = 0;
            if (events.Count > 0)
            {
                var next = events.Dequeue();
                clientId = next.TransportId;
                return next.Kind;
            }
            if (pendingConnection != 0)
            {
                clientId = pendingConnection;
                payload = new ArraySegment<byte>(buffer, 0, pendingSize);
                pendingConnection = 0;
                return NetworkEvent.Data;
            }
            uint source = hosting ? pollGroup : hostConnection;
            if (source == 0 || !Sockets.TryReceive(source, hosting, ref buffer, out int size, out uint connection))
                return NetworkEvent.Nothing;
            if (!hosting)
            {
                clientId = HostTransportId;
                payload = new ArraySegment<byte>(buffer, 0, size);
                return NetworkEvent.Data;
            }
            clientId = connection;
            // Steam can deliver a connection's first message before its Connected callback runs.
            if (!steamIds.ContainsKey(connection))
            {
                steamIds[connection] = Sockets.GetRemoteSteamId(connection);
                pendingConnection = connection;
                pendingSize = size;
                return NetworkEvent.Connect;
            }
            payload = new ArraySegment<byte>(buffer, 0, size);
            return NetworkEvent.Data;
        }

        /// <inheritdoc/>
        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (steamIds.Remove(clientId)) Sockets.Close((uint)clientId, true);
            rtts.Remove((uint)clientId);
        }

        /// <inheritdoc/>
        public override void DisconnectLocalClient()
        {
            if (hostConnection != 0) Sockets.Close(hostConnection, true);
            rtts.Remove(hostConnection);
            hostConnection = 0;
        }

        /// <inheritdoc/>
        public override ulong GetCurrentRtt(ulong clientId)
        {
            uint connection = joined ? hostConnection : (uint)clientId;
            if (connection == 0) return 0;
            // Steam's status query allocates managed arrays, and NGO asks every frame.
            double now = Clock();
            if (rtts.TryGetValue(connection, out var cached) && now - cached.At < RttRefreshSeconds) return cached.Rtt;
            ulong rtt = (ulong)Math.Max(0, Sockets.GetPing(connection));
            rtts[connection] = (now, rtt);
            return rtt;
        }

        /// <inheritdoc/>
        public override void Shutdown()
        {
            if (sockets == null) return;
            sockets.StatusChanged -= OnStatusChanged;
            foreach (var pair in steamIds)
                if (pair.Key != HostTransportId) sockets.Close((uint)pair.Key, true);
            if (hostConnection != 0) sockets.Close(hostConnection, true);
            if (listenSocket != 0) sockets.CloseListenSocket(listenSocket);
            if (pollGroup != 0) sockets.DestroyPollGroup(pollGroup);
            sockets.DiscardReceived();
            steamIds.Clear();
            rtts.Clear();
            events.Clear();
            listenSocket = 0;
            pollGroup = 0;
            hostConnection = 0;
            pendingConnection = 0;
            hosting = false;
            joined = false;
        }

        internal static int SendFlags(NetworkDelivery delivery) =>
            (delivery == NetworkDelivery.Unreliable || delivery == NetworkDelivery.UnreliableSequenced
                ? Constants.k_nSteamNetworkingSend_Unreliable
                : Constants.k_nSteamNetworkingSend_Reliable) | Constants.k_nSteamNetworkingSend_NoNagle;

        internal void OnDestroy()
        {
            Shutdown();
            (sockets as IDisposable)?.Dispose();
        }

        private void OnStatusChanged(SteamConnectionStatus status)
        {
            switch (status.State)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    if (!hosting || status.ListenSocket != listenSocket) return;
                    if ((AcceptFilter == null || AcceptFilter(status.RemoteSteamId)) && Sockets.Accept(status.Connection))
                        Sockets.SetPollGroup(status.Connection, pollGroup);
                    else
                        Sockets.Close(status.Connection, false);
                    return;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (hosting && status.ListenSocket == listenSocket)
                    {
                        if (steamIds.ContainsKey(status.Connection)) return;
                        steamIds[status.Connection] = status.RemoteSteamId;
                        events.Enqueue(new PendingEvent(NetworkEvent.Connect, status.Connection));
                    }
                    else if (joined && status.Connection == hostConnection)
                    {
                        events.Enqueue(new PendingEvent(NetworkEvent.Connect, HostTransportId));
                    }
                    return;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    Sockets.Close(status.Connection, false);
                    rtts.Remove(status.Connection);
                    var reason = status.State == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                        ? DisconnectEvents.ClosedByRemote
                        : DisconnectEvents.ProtocolTimeout;
                    if (hosting && steamIds.Remove(status.Connection))
                    {
                        SetDisconnectEvent(reason);
                        events.Enqueue(new PendingEvent(NetworkEvent.Disconnect, status.Connection));
                    }
                    else if (joined && status.Connection == hostConnection)
                    {
                        hostConnection = 0;
                        SetDisconnectEvent(reason);
                        events.Enqueue(new PendingEvent(NetworkEvent.Disconnect, HostTransportId));
                    }
                    return;
            }
        }

        private readonly struct PendingEvent
        {
            public PendingEvent(NetworkEvent kind, ulong transportId)
            {
                Kind = kind;
                TransportId = transportId;
            }

            public NetworkEvent Kind { get; }
            public ulong TransportId { get; }
        }
    }
}
