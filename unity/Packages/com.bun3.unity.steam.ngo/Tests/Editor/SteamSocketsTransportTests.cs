using System;
using NUnit.Framework;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Steam.Ngo.Tests
{
    public class SteamSocketsTransportTests
    {
        private const ESteamNetworkingConnectionState Connecting = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting;
        private const ESteamNetworkingConnectionState Connected = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;
        private const ESteamNetworkingConnectionState ClosedByPeer = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer;
        private const ESteamNetworkingConnectionState ProblemDetectedLocally = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally;

        private SteamSocketsTransport transport;
        private FakeSteamSockets sockets;

        [SetUp]
        public void SetUp()
        {
            transport = new GameObject("Steam transport").AddComponent<SteamSocketsTransport>();
            sockets = new FakeSteamSockets();
            transport.Sockets = sockets;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(transport.gameObject);

        private NetworkEvent Poll(out ulong id, out ArraySegment<byte> payload) => transport.PollEvent(out id, out payload, out _);

        private void ConnectPeer(uint connection, ulong steamId)
        {
            sockets.Raise(connection, steamId, Connecting, sockets.ListenSocket);
            sockets.Raise(connection, steamId, Connected, sockets.ListenSocket);
            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Connect));
            Assert.That(id, Is.EqualTo((ulong)connection));
        }

        [Test]
        public void HostAcceptsAllowedPeersAndReportsThemByConnection()
        {
            transport.AcceptFilter = steamId => steamId == 7;
            Assert.That(transport.StartServer(), Is.True);
            Assert.That(sockets.RelayInits, Is.EqualTo(1));

            sockets.Raise(101, 7, Connecting, sockets.ListenSocket);
            sockets.Raise(102, 8, Connecting, sockets.ListenSocket);
            Assert.That(sockets.Accepted, Is.EqualTo(new uint[] { 101 }));
            Assert.That(sockets.Closed, Is.EqualTo(new uint[] { 102 }));
            Assert.That(sockets.Groups[101], Is.EqualTo(sockets.PollGroup));

            sockets.Raise(101, 7, Connected, sockets.ListenSocket);
            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Connect));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(transport.TryGetSteamId(101, out ulong steamId), Is.True);
            Assert.That(steamId, Is.EqualTo(7ul));
            Assert.That(Poll(out _, out _), Is.EqualTo(NetworkEvent.Nothing));
        }

        [Test]
        public void ReceivedMessagesShareOneBuffer()
        {
            transport.StartServer();
            ConnectPeer(101, 7);
            sockets.Inbox.Enqueue((101, new byte[] { 1, 2, 3 }));
            sockets.Inbox.Enqueue((101, new byte[] { 4, 5 }));

            Assert.That(Poll(out ulong id, out var first), Is.EqualTo(NetworkEvent.Data));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(first.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            byte[] array = first.Array;

            Assert.That(Poll(out _, out var second), Is.EqualTo(NetworkEvent.Data));
            Assert.That(second.ToArray(), Is.EqualTo(new byte[] { 4, 5 }));
            Assert.That(second.Array, Is.SameAs(array));
        }

        [Test]
        public void FirstMessageBeforeConnectedCallbackStillFollowsAConnectEvent()
        {
            transport.StartServer();
            sockets.Raise(101, 7, Connecting, sockets.ListenSocket);
            sockets.RemoteIds[101] = 7;
            sockets.Inbox.Enqueue((101, new byte[] { 9 }));

            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Connect));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(Poll(out id, out var payload), Is.EqualTo(NetworkEvent.Data));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(payload.ToArray(), Is.EqualTo(new byte[] { 9 }));

            sockets.Raise(101, 7, Connected, sockets.ListenSocket);
            Assert.That(Poll(out _, out _), Is.EqualTo(NetworkEvent.Nothing), "A late Connected callback must not connect twice.");
            Assert.That(transport.TryGetSteamId(101, out ulong steamId), Is.True);
            Assert.That(steamId, Is.EqualTo(7ul));
        }

        [Test]
        public void PeerClosingAfterConnectRaisesOneDisconnect()
        {
            transport.StartServer();
            ConnectPeer(101, 7);
            sockets.Raise(101, 7, ClosedByPeer, sockets.ListenSocket);
            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Disconnect));
            Assert.That(id, Is.EqualTo(101ul));
            Assert.That(transport.DisconnectEvent, Is.EqualTo(NetworkTransport.DisconnectEvents.ClosedByRemote));
            Assert.That(sockets.Closed, Does.Contain(101u));

            sockets.Raise(103, 9, ClosedByPeer, sockets.ListenSocket);
            Assert.That(Poll(out _, out _), Is.EqualTo(NetworkEvent.Nothing), "A connection that never connected has nothing to report.");
        }

        [Test]
        public void ClientConnectsToTheTargetAndTalksToTheHost()
        {
            Assert.That(transport.StartClient(), Is.False, "No target yet.");
            transport.TargetSteamId = 55;
            Assert.That(transport.StartClient(), Is.True);
            Assert.That(sockets.ConnectedTo, Is.EqualTo(55ul));

            sockets.Raise(sockets.OutgoingConnection, 55, Connected, 0);
            Assert.That(Poll(out ulong id, out _), Is.EqualTo(NetworkEvent.Connect));
            Assert.That(id, Is.EqualTo(transport.ServerClientId));
            Assert.That(transport.TryGetSteamId(transport.ServerClientId, out ulong host), Is.True);
            Assert.That(host, Is.EqualTo(55ul));

            transport.Send(transport.ServerClientId, new ArraySegment<byte>(new byte[] { 1, 2 }, 1, 1), NetworkDelivery.Unreliable);
            Assert.That(sockets.Sent[0].Connection, Is.EqualTo(sockets.OutgoingConnection));
            Assert.That(sockets.Sent[0].Data, Is.EqualTo(new byte[] { 2 }));

            sockets.Inbox.Enqueue((sockets.OutgoingConnection, new byte[] { 8 }));
            Assert.That(Poll(out id, out var payload), Is.EqualTo(NetworkEvent.Data));
            Assert.That(id, Is.EqualTo(transport.ServerClientId));
            Assert.That(payload.ToArray(), Is.EqualTo(new byte[] { 8 }));
            Assert.That(transport.GetCurrentRtt(transport.ServerClientId), Is.EqualTo(42ul));

            sockets.Raise(sockets.OutgoingConnection, 55, ProblemDetectedLocally, 0);
            Assert.That(Poll(out id, out _), Is.EqualTo(NetworkEvent.Disconnect));
            Assert.That(id, Is.EqualTo(transport.ServerClientId));
            Assert.That(transport.DisconnectEvent, Is.EqualTo(NetworkTransport.DisconnectEvents.ProtocolTimeout));
        }

        [Test]
        public void RttIsCachedBetweenRefreshes()
        {
            double now = 100;
            transport.Clock = () => now;
            transport.TargetSteamId = 55;
            transport.StartClient();

            Assert.That(transport.GetCurrentRtt(transport.ServerClientId), Is.EqualTo(42ul));
            now += 0.5;
            transport.GetCurrentRtt(transport.ServerClientId);
            Assert.That(sockets.PingCalls, Is.EqualTo(1), "Within the interval the cached value is returned.");

            now += 1;
            transport.GetCurrentRtt(transport.ServerClientId);
            Assert.That(sockets.PingCalls, Is.EqualTo(2), "After the interval Steam is queried again.");
        }

        [TestCase(NetworkDelivery.Reliable, true)]
        [TestCase(NetworkDelivery.ReliableSequenced, true)]
        [TestCase(NetworkDelivery.ReliableFragmentedSequenced, true)]
        [TestCase(NetworkDelivery.Unreliable, false)]
        [TestCase(NetworkDelivery.UnreliableSequenced, false)]
        public void DeliveryMapsToReliableOrUnreliableWithoutNagle(NetworkDelivery delivery, bool reliable)
        {
            int flags = SteamSocketsTransport.SendFlags(delivery);
            Assert.That((flags & Constants.k_nSteamNetworkingSend_Reliable) != 0, Is.EqualTo(reliable));
            Assert.That(flags & Constants.k_nSteamNetworkingSend_NoNagle, Is.Not.Zero);
        }

        [Test]
        public void NothingStartsWithoutSteam()
        {
            sockets.IsAvailable = false;
            Assert.That(transport.StartServer(), Is.False);
            transport.TargetSteamId = 1;
            Assert.That(transport.StartClient(), Is.False);
            Assert.That(sockets.RelayInits, Is.Zero);
        }

        [Test]
        public void ShutdownClosesConnectionsAndTheListenSocket()
        {
            transport.StartServer();
            ConnectPeer(101, 7);
            transport.Shutdown();
            Assert.That(sockets.Closed, Does.Contain(101u));
            Assert.That(sockets.ListenSocketClosed, Is.True);
            Assert.That(transport.TryGetSteamId(101, out _), Is.False);
            Assert.That(transport.StartServer(), Is.True, "A shut down transport can host again.");
        }
    }
}
