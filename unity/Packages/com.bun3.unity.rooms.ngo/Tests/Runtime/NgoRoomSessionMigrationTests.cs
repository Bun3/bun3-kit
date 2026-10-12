using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Bun3.Unity.Rooms.Ngo.Tests
{
    public class NgoRoomSessionMigrationTests
    {
        private sealed class Peer
        {
            public NetworkManager Manager;
            public FakeRoom Room;
            public NgoRoomSession Session;
            public int Migrations;
        }

        private static NgoRoomSessionOptions Options(bool canMigrate, double timeoutSeconds = 15) => new NgoRoomSessionOptions
        {
            CanMigrate = () => canMigrate,
            MigrationTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        private static async UniTask<Peer[]> StartRoomAsync(FakeRoomHub hub, NgoRoomSessionOptions options, params string[] ids)
        {
            var peers = new Peer[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var manager = NgoTestPeers.Create(ids[i]);
                var room = hub.Join(ids[i]);
                var peer = new Peer { Manager = manager, Room = room };
                peer.Session = new NgoRoomSession(manager, room, NgoTestPeers.Transport(manager), options);
                peer.Session.HostMigrated += () => peer.Migrations++;
                peers[i] = peer;
                await peer.Session.StartAsync().Timeout(TimeSpan.FromSeconds(10));
            }
            return peers;
        }

        private static UniTask WaitAsync(Func<bool> condition, double seconds = 20) =>
            UniTask.WaitUntil(condition).Timeout(TimeSpan.FromSeconds(seconds));

        private static NetworkManager[] Managers(Peer[] peers) => Array.ConvertAll(peers, peer => peer.Manager);

        [UnityTest]
        public IEnumerator CrashedHostIsReplacedByOldestRemainingMember() => UniTask.ToCoroutine(async () =>
        {
            var hub = new FakeRoomHub();
            Peer[] peers = null;
            try
            {
                peers = await StartRoomAsync(hub, Options(true), "A", "B", "C");
                Assert.That(hub.Data[NgoRoomSession.HostEpochKey], Is.EqualTo("1"));
                Assert.That(hub.Data[NgoRoomSession.HostMemberKey], Is.EqualTo("A"));

                hub.Remove("A");               // ownership falls to the newest member, C
                peers[0].Manager.Shutdown();   // crash without LeaveAsync

                await WaitAsync(() => peers[1].Migrations == 1 && peers[2].Migrations == 1 &&
                    peers[1].Session.State == RoomSessionState.Connected && peers[2].Session.State == RoomSessionState.Connected);
                Assert.That(hub.OwnerId, Is.EqualTo("B"), "The newest member hands ownership to the oldest.");
                Assert.That(hub.Data[NgoRoomSession.HostMemberKey], Is.EqualTo("B"));
                Assert.That(hub.Data[NgoRoomSession.HostEpochKey], Is.EqualTo("2"));
                Assert.That(peers[1].Manager.IsHost, Is.True);
                await WaitAsync(() => peers[1].Manager.ConnectedClientsIds.Count == 2);
                Assert.That(peers[2].Manager.IsConnectedClient, Is.True);
            }
            finally
            {
                if (peers != null) await NgoTestPeers.DestroyAsync(Managers(peers));
            }
        });

        [UnityTest]
        public IEnumerator LeavingOwnerHandsTheRoomToTheOldestMember() => UniTask.ToCoroutine(async () =>
        {
            var hub = new FakeRoomHub();
            Peer[] peers = null;
            try
            {
                peers = await StartRoomAsync(hub, Options(true), "A", "B", "C");
                await peers[0].Session.LeaveAsync();
                Assert.That(peers[0].Session.State, Is.EqualTo(RoomSessionState.Closed));
                Assert.That(hub.Members.Exists(member => member.Id == "A"), Is.False);
                Assert.That(hub.OwnerId, Is.EqualTo("B"));

                await WaitAsync(() => peers[1].Migrations == 1 && peers[2].Migrations == 1 &&
                    peers[2].Session.State == RoomSessionState.Connected);
                Assert.That(hub.OwnerId, Is.EqualTo("B"), "Nobody may hand the room back to the member who left.");
                Assert.That(peers[1].Manager.IsHost, Is.True);
                Assert.That(hub.Data[NgoRoomSession.HostEpochKey], Is.EqualTo("2"));
            }
            finally
            {
                if (peers != null) await NgoTestPeers.DestroyAsync(Managers(peers));
            }
        });

        [UnityTest]
        public IEnumerator HostLossClosesSessionsWhenMigrationIsNotAllowed() => UniTask.ToCoroutine(async () =>
        {
            var hub = new FakeRoomHub();
            Peer[] peers = null;
            try
            {
                peers = await StartRoomAsync(hub, Options(false), "A", "B");
                peers[0].Manager.Shutdown();
                await WaitAsync(() => peers[1].Session.State == RoomSessionState.Closed);
                Assert.That(peers[1].Manager.IsListening, Is.False);
                Assert.That(peers[1].Migrations, Is.EqualTo(0));
                Assert.That(hub.Members.Exists(member => member.Id == "B"), Is.True, "Leaving the room stays the game's decision.");
            }
            finally
            {
                if (peers != null) await NgoTestPeers.DestroyAsync(Managers(peers));
            }
        });

        [UnityTest]
        public IEnumerator MigrationFailsWhenNoHostAppearsInTime() => UniTask.ToCoroutine(async () =>
        {
            var hub = new FakeRoomHub();
            Peer[] peers = null;
            try
            {
                peers = await StartRoomAsync(hub, Options(true, 3), "A", "B", "C");
                hub.IgnoreTransfers = true;
                hub.Remove("A");
                peers[0].Manager.Shutdown();

                await WaitAsync(() => peers[1].Session.State == RoomSessionState.Failed &&
                    peers[2].Session.State == RoomSessionState.Failed, 12);
                Assert.That(peers[1].Manager.IsListening, Is.False);
                Assert.That(peers[2].Manager.IsListening, Is.False);
                Assert.That(hub.Members.Count, Is.EqualTo(2), "A failed migration keeps everyone in the room.");
            }
            finally
            {
                if (peers != null) await NgoTestPeers.DestroyAsync(Managers(peers));
            }
        });
    }
}
