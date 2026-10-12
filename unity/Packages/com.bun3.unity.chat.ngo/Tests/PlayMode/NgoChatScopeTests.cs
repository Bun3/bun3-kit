#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Bun3.Chat;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Chat.Ngo.Tests
{
    public sealed class NgoChatScopeTests
    {
        sealed class Policy : IChatPolicy
        {
            public bool AllowSend = true;
            public byte Channel = 1;
            public readonly HashSet<ulong> Deaf = new HashSet<ulong>();
            public readonly HashSet<byte> HiddenChannels = new HashSet<byte>();

            public bool TryGetSender(ulong senderId, out string playerId, out string name)
            {
                playerId = "player-" + senderId;
                name = "Name" + senderId;
                return true;
            }

            public bool TryGetChannel(ulong senderId, out byte channel)
            {
                channel = Channel;
                return AllowSend;
            }

            public bool CanReceive(ulong recipientId, ulong senderId, byte channel) =>
                !Deaf.Contains(recipientId) && !HiddenChannels.Contains(channel);
        }

        sealed class Peer
        {
            public NetworkManager Manager = null!;
            public ChatRelay Relay = null!;
            public NgoChatScope Scope = null!;
            public readonly List<ChatMessage> Live = new List<ChatMessage>();
            public readonly List<string> Events = new List<string>();
        }

        readonly List<Peer> peers = new List<Peer>();
        bool priorRunInBackground;

        [SetUp]
        public void SetUp()
        {
            priorRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var peer in peers)
            {
                peer.Scope?.Dispose();
                if (peer.Manager != null && peer.Manager.IsListening) peer.Manager.Shutdown();
            }
            yield return null;
            yield return null;
            foreach (var peer in peers)
                if (peer.Manager != null) Object.Destroy(peer.Manager.gameObject);
            peers.Clear();
            yield return null;
            Application.runInBackground = priorRunInBackground;
        }

        static ushort FreePort()
        {
            using var socket = new UdpClient(0);
            return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
        }

        Peer Create(string name, ushort port, Policy policy, ChatRelayOptions? options = null, Action<NetworkManager>? beforeScope = null, bool attachScope = true)
        {
            var root = new GameObject(name);
            var transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
            var manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
            beforeScope?.Invoke(manager);
            var peer = new Peer { Manager = manager, Relay = new ChatRelay(policy, options) };
            peers.Add(peer);
            if (attachScope) AttachScope(peer);
            return peer;
        }

        static void AttachScope(Peer peer)
        {
            peer.Scope = new NgoChatScope(peer.Manager, peer.Relay);
            peer.Scope.Received += message =>
            {
                peer.Live.Add(message);
                peer.Events.Add("live:" + message.Text);
            };
            peer.Scope.HistoryReceived += message => peer.Events.Add("history:" + message.Text);
        }

        static IEnumerator Until(Func<bool> condition, float seconds = 10)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) Assert.Fail("Timed out waiting for the network.");
                yield return null;
            }
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        IEnumerator HostAndClient(Policy policy, ChatRelayOptions? options, Action<Peer, Peer> ready)
        {
            ushort port = FreePort();
            var host = Create("Host", port, policy, options);
            Assert.That(host.Manager.StartHost(), Is.True);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            ready(host, client);
        }

        [UnityTest]
        public IEnumerator ClientSendReachesBothWithHostPolicyIdentity()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            Assert.That(client.Scope.Send("  hello\n"), Is.True);
            yield return Until(() => host.Live.Count == 1 && client.Live.Count == 1);
            var message = client.Live[0];
            ulong id = client.Manager.LocalClientId;
            Assert.That(message.Text, Is.EqualTo("hello"));
            Assert.That(message.SenderId, Is.EqualTo(id));
            Assert.That(message.SenderPlayerId, Is.EqualTo("player-" + id));
            Assert.That(message.SenderName, Is.EqualTo("Name" + id));
        }

        [UnityTest]
        public IEnumerator HostSendUsesTheLocalPath()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            Assert.That(host.Scope.Send("from host"), Is.True);
            yield return Until(() => host.Live.Count == 1 && client.Live.Count == 1);
            Assert.That(client.Live[0].SenderId, Is.EqualTo(NetworkManager.ServerClientId));
        }

        [UnityTest]
        public IEnumerator ScopeCreatedWhileListeningDeliversToAlreadyConnectedClients()
        {
            ushort port = FreePort();
            var host = Create("Host", port, new Policy(), attachScope: false);
            Assert.That(host.Manager.StartHost(), Is.True);
            var client = Create("Client", port, new Policy());
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient && host.Manager.ConnectedClientsIds.Count == 2);
            AttachScope(host);
            Assert.That(host.Scope.Send("late"), Is.True);
            yield return Until(() => host.Live.Count == 1 && client.Live.Count == 1);
            Assert.That(client.Live[0].Text, Is.EqualTo("late"));
        }

        [UnityTest]
        public IEnumerator ThrowingHostHandlerDoesNotBlockRemoteDelivery()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            host.Scope.Received += _ => throw new InvalidOperationException("boom");
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            Assert.That(client.Scope.Send("hello"), Is.True);
            yield return Until(() => client.Live.Count == 1);
            Assert.That(client.Live[0].Text, Is.EqualTo("hello"));
        }

        [UnityTest]
        public IEnumerator PolicyCanExcludeARecipient()
        {
            var policy = new Policy();
            Peer host = null!, client = null!;
            yield return HostAndClient(policy, null, (h, c) => { host = h; client = c; });
            policy.Deaf.Add(client.Manager.LocalClientId);
            client.Scope.Send("muted");
            yield return Until(() => host.Live.Count == 1);
            yield return Frames(10);
            Assert.That(client.Live, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DeniedChannelReachesNobody()
        {
            var policy = new Policy { AllowSend = false };
            Peer host = null!, client = null!;
            yield return HostAndClient(policy, null, (h, c) => { host = h; client = c; });
            Assert.That(client.Scope.Send("denied"), Is.True);
            yield return Frames(20);
            Assert.That(host.Live, Is.Empty);
            Assert.That(client.Live, Is.Empty);
        }

        [UnityTest]
        public IEnumerator RestartRegistersHandlersAgain()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            client.Manager.Shutdown();
            host.Manager.Shutdown();
            yield return Until(() => !host.Manager.IsListening && !client.Manager.IsListening &&
                !host.Manager.ShutdownInProgress && !client.Manager.ShutdownInProgress);
            Assert.That(host.Manager.StartHost(), Is.True);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            client.Scope.Send("again");
            yield return Until(() => host.Live.Count == 1 && client.Live.Count == 1);
        }

        [UnityTest]
        public IEnumerator DisposeStopsEvents()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            client.Scope.Dispose();
            Assert.That(client.Scope.Send("after dispose"), Is.False);
            host.Scope.Send("unheard");
            yield return Until(() => host.Live.Count == 1);
            yield return Frames(10);
            Assert.That(client.Live, Is.Empty);
        }

        [UnityTest]
        public IEnumerator SendBeforeConnectingReturnsFalse()
        {
            var peer = Create("Idle", FreePort(), new Policy());
            Assert.That(peer.Scope.Send("not listening"), Is.False);
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewClientGetsRecentHistoryBeforeLiveMessages()
        {
            var options = new ChatRelayOptions { HistoryCapacity = 10, HistoryReplayCount = 2 };
            ushort port = FreePort();
            var host = Create("Host", port, new Policy(), options);
            Assert.That(host.Manager.StartHost(), Is.True);
            foreach (var text in new[] { "one", "two", "three" }) host.Scope.Send(text);
            yield return Until(() => host.Live.Count == 3);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Events.Count == 2);
            host.Scope.Send("four");
            yield return Until(() => client.Events.Count == 3);
            Assert.That(client.Events, Is.EqualTo(new[] { "history:two", "history:three", "live:four" }));
        }

        [UnityTest]
        public IEnumerator MessageSentBeforeReplayIsNotDeliveredLive()
        {
            var options = new ChatRelayOptions { HistoryCapacity = 10, HistoryReplayCount = 10 };
            ushort port = FreePort();
            Peer host = null!;
            host = Create("Host", port, new Policy(), options, m => m.OnClientConnectedCallback += id =>
            {
                if (id != m.LocalClientId) host.Scope.Send("during");
            });
            Assert.That(host.Manager.StartHost(), Is.True);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Events.Count == 1);
            host.Scope.Send("after");
            yield return Until(() => client.Events.Count == 2);
            yield return Frames(10);
            Assert.That(client.Events, Is.EqualTo(new[] { "history:during", "live:after" }));
        }

        [UnityTest]
        public IEnumerator ReplayCountZeroSendsOnlyLaterMessages()
        {
            var options = new ChatRelayOptions { HistoryCapacity = 10, HistoryReplayCount = 0 };
            ushort port = FreePort();
            var host = Create("Host", port, new Policy(), options);
            Assert.That(host.Manager.StartHost(), Is.True);
            host.Scope.Send("before");
            yield return Until(() => host.Live.Count == 1);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            host.Scope.Send("after");
            yield return Until(() => client.Events.Count == 1);
            Assert.That(client.Events, Is.EqualTo(new[] { "live:after" }));
        }

        [UnityTest]
        public IEnumerator HiddenChannelIsNotReplayed()
        {
            var policy = new Policy();
            var options = new ChatRelayOptions { HistoryCapacity = 10, HistoryReplayCount = 10 };
            ushort port = FreePort();
            var host = Create("Host", port, policy, options);
            Assert.That(host.Manager.StartHost(), Is.True);
            policy.Channel = 2;
            host.Scope.Send("secret");
            policy.Channel = 1;
            host.Scope.Send("public");
            yield return Until(() => host.Live.Count == 2);
            policy.HiddenChannels.Add(2);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            host.Scope.Send("live");
            yield return Until(() => client.Events.Contains("live:live"));
            Assert.That(client.Events, Is.EqualTo(new[] { "history:public", "live:live" }));
        }

        [UnityTest]
        public IEnumerator LargeHistoryArrivesInOrderAcrossChunks()
        {
            var options = new ChatRelayOptions { HistoryCapacity = 60, HistoryReplayCount = 60 };
            ushort port = FreePort();
            var host = Create("Host", port, new Policy(), options);
            Assert.That(host.Manager.StartHost(), Is.True);
            var seed = new ChatLog(60);
            var expected = new string[60];
            for (int i = 0; i < 60; i++)
            {
                expected[i] = "history:" + i.ToString("D3") + new string('x', 97);
                seed.Add(new ChatMessage(0, "player-0", "Name0", i.ToString("D3") + new string('x', 97), 1, i));
            }
            host.Relay.SeedHistory(seed);   // 60 x ~136 bytes: at least two 4 KB chunks
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Events.Count == 60);
            Assert.That(client.Events, Is.EqualTo(expected));
        }

        [UnityTest]
        public IEnumerator RestartedServerStartsWithEmptyHistory()
        {
            var options = new ChatRelayOptions { HistoryCapacity = 10, HistoryReplayCount = 10 };
            ushort port = FreePort();
            var host = Create("Host", port, new Policy(), options);
            Assert.That(host.Manager.StartHost(), Is.True);
            host.Scope.Send("old");
            yield return Until(() => host.Live.Count == 1);
            host.Manager.Shutdown();
            yield return Until(() => !host.Manager.IsListening && !host.Manager.ShutdownInProgress);
            Assert.That(host.Relay.History!.Count, Is.EqualTo(0));
            Assert.That(host.Manager.StartHost(), Is.True);
            var client = Create("Client", port, new Policy(), options);
            Assert.That(client.Manager.StartClient(), Is.True);
            yield return Until(() => client.Manager.IsConnectedClient);
            host.Scope.Send("new");
            yield return Until(() => client.Events.Count == 1);
            Assert.That(client.Events, Is.EqualTo(new[] { "live:new" }));
        }

        [UnityTest]
        public IEnumerator MalformedSubmitIsDroppedSilently()
        {
            Peer host = null!, client = null!;
            yield return HostAndClient(new Policy(), null, (h, c) => { host = h; client = c; });
            using (var oversized = new FastBufferWriter(4, Allocator.Temp))
            {
                oversized.WriteValueSafe((ushort)60000);
                client.Manager.CustomMessagingManager.SendNamedMessage("Bun3.Chat.Submit", NetworkManager.ServerClientId, oversized, NetworkDelivery.ReliableFragmentedSequenced);
            }
            using (var truncated = new FastBufferWriter(8, Allocator.Temp))
            {
                truncated.WriteValueSafe((ushort)50);
                truncated.WriteBytesSafe(Encoding.UTF8.GetBytes("abc"));
                client.Manager.CustomMessagingManager.SendNamedMessage("Bun3.Chat.Submit", NetworkManager.ServerClientId, truncated, NetworkDelivery.ReliableFragmentedSequenced);
            }
            client.Scope.Send("valid");
            yield return Until(() => host.Live.Count == 1);
            Assert.That(host.Live[0].Text, Is.EqualTo("valid"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OversizedMaxLengthIsRejected()
        {
            var root = new GameObject("Manager");
            try
            {
                var manager = root.AddComponent<NetworkManager>();
                Assert.Throws<ArgumentOutOfRangeException>(() => new NgoChatScope(manager, new ChatRelay(new Policy(), new ChatRelayOptions { MaxLength = 897 })));
                using var fits = new NgoChatScope(manager, new ChatRelay(new Policy(), new ChatRelayOptions { MaxLength = 896 }));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
