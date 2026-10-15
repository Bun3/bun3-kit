using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Rooms.Ngo.Tests
{
    public class LoopbackRoomBackendTests
    {
        [UnityTest]
        public IEnumerator TypedCodeConnectsToTheLoopbackHost() => UniTask.ToCoroutine(async () =>
        {
            var host = NgoTestPeers.Create("Host");
            var client = NgoTestPeers.Create("Client");
            try
            {
                var hostTransport = NgoTestPeers.Transport(host);
                IRoom hostRoom = await new LoopbackRoomBackend(host, hostTransport)
                    .CreateAsync(new RoomCreateOptions { MaxMembers = 4, Name = "Host" });
                var hostSession = new NgoRoomSession(host, hostRoom, hostTransport);
                await hostSession.StartAsync();
                Assert.That(hostSession.State, Is.EqualTo(RoomSessionState.Connected));
                Assert.That(hostTransport.Transport.ConnectionData.Port, Is.EqualTo((ushort)hostRoom.Code.Value));
                Assert.That(hostRoom.GetData(NgoRoomSession.HostKey), Is.EqualTo("127.0.0.1:" + hostRoom.Code.Value));
                Assert.That(hostRoom.Name, Is.EqualTo("Host"));
                Assert.That(hostRoom.MaxMembers, Is.EqualTo(4));
                int dataChanges = 0;
                hostRoom.DataChanged += () => dataChanges++;
                await hostRoom.SetJoinableAsync(false);
                Assert.That(hostRoom.IsJoinable, Is.False);
                Assert.That(dataChanges, Is.EqualTo(1));
                await hostRoom.SetJoinableAsync(true);
                Assert.That(dataChanges, Is.EqualTo(2));
                await hostRoom.SetDataAsync("k", "v");
                Assert.That(dataChanges, Is.EqualTo(3), "Host data writes raise DataChanged.");

                Assert.That(RoomCode.TryNormalize(hostRoom.Code.ToString().ToLowerInvariant(), out var typed), Is.True);
                var clientTransport = NgoTestPeers.Transport(client);
                IRoom clientRoom = await new LoopbackRoomBackend(client, clientTransport).JoinByCodeAsync(typed, default);
                int clientDataChanges = 0;
                clientRoom.DataChanged += () => clientDataChanges++;
                var clientSession = new NgoRoomSession(client, clientRoom, clientTransport);
                await clientSession.StartAsync().Timeout(TimeSpan.FromSeconds(10));

                Assert.That(clientSession.State, Is.EqualTo(RoomSessionState.Connected));
                Assert.That(clientRoom.LocalMemberId, Is.EqualTo("local-1"));
                Assert.That(clientRoom.MaxMembers, Is.EqualTo(0), "A loopback client does not know the capacity.");
                Assert.That(clientRoom.IsJoinable, Is.True);
                await UniTask.WaitUntil(() => hostRoom.Members.Count == 2).Timeout(TimeSpan.FromSeconds(5));
                Assert.That(hostRoom.Members[1].Id, Is.EqualTo(clientRoom.LocalMemberId));
                Assert.That(clientRoom.Members[0].Id, Is.EqualTo(hostRoom.LocalMemberId));
                Assert.That(hostRoom.LocalMemberId, Is.EqualTo("local-0"));

                bool closed = false;
                clientRoom.Closed += () => closed = true;
                await hostSession.LeaveAsync();
                await UniTask.WaitUntil(() => clientSession.State == RoomSessionState.Closed).Timeout(TimeSpan.FromSeconds(10));
                Assert.That(closed, Is.True);
                Assert.That(clientDataChanges, Is.EqualTo(0), "A loopback client never raises DataChanged.");
            }
            finally
            {
                await NgoTestPeers.DestroyAsync(host, client);
            }
        });

        [UnityTest]
        public IEnumerator ClientLeavingDoesNotRaiseClosed() => UniTask.ToCoroutine(async () =>
        {
            var host = NgoTestPeers.Create("Host");
            var client = NgoTestPeers.Create("Client");
            try
            {
                var hostTransport = NgoTestPeers.Transport(host);
                IRoom hostRoom = await new LoopbackRoomBackend(host, hostTransport).CreateAsync(new RoomCreateOptions { MaxMembers = 4 });
                await new NgoRoomSession(host, hostRoom, hostTransport).StartAsync();

                var clientTransport = NgoTestPeers.Transport(client);
                IRoom clientRoom = await new LoopbackRoomBackend(client, clientTransport).JoinByCodeAsync(hostRoom.Code, default);
                var clientSession = new NgoRoomSession(client, clientRoom, clientTransport);
                await clientSession.StartAsync().Timeout(TimeSpan.FromSeconds(10));
                bool closed = false;
                clientRoom.Closed += () => closed = true;

                await clientSession.LeaveAsync();
                await UniTask.DelayFrame(5);
                Assert.That(closed, Is.False, "Closed is only for rooms that end without LeaveAsync.");
                Assert.That(client.IsListening, Is.False);
            }
            finally
            {
                await NgoTestPeers.DestroyAsync(host, client);
            }
        });

        [Test]
        public void CodesOutsideThePortRangeAreNotFound()
        {
            var manager = NgoTestPeers.Create("Loopback");
            try
            {
                var backend = new LoopbackRoomBackend(manager, NgoTestPeers.Transport(manager));
                var zero = Assert.Throws<RoomException>(() => backend.JoinByCodeAsync(RoomCode.FromValue(0), default));
                Assert.That(zero.Failure, Is.EqualTo(RoomFailure.NotFound));
                var high = Assert.Throws<RoomException>(() => backend.JoinByCodeAsync(RoomCode.FromValue(70000), default));
                Assert.That(high.Failure, Is.EqualTo(RoomFailure.NotFound));
                var id = Assert.Throws<RoomException>(() => backend.JoinByIdAsync("not-a-code", default));
                Assert.That(id.Failure, Is.EqualTo(RoomFailure.NotFound));
            }
            finally
            {
                Object.Destroy(manager.gameObject);
            }
        }

        private static string NewRegistry() => Path.Combine(Path.GetTempPath(), "bun3-rooms-test-" + Guid.NewGuid().ToString("N"));

        private static void DeleteRegistry(string registry)
        {
            if (Directory.Exists(registry)) Directory.Delete(registry, true);
        }

        private static RoomListOptions List(string version = "0.1") =>
            new RoomListOptions { Version = version, DataKeys = new[] { "jp.map" } };

        [UnityTest]
        public IEnumerator PublicRoomIsListedUntilItIsPrivateOrClosed() => UniTask.ToCoroutine(async () =>
        {
            string registry = NewRegistry();
            var host = NgoTestPeers.Create("Host");
            try
            {
                var transport = NgoTestPeers.Transport(host);
                var backend = new LoopbackRoomBackend(host, transport, registry);
                IRoom room = await backend.CreateAsync(new RoomCreateOptions { MaxMembers = 4, Name = "Host", Version = "0.1" });
                await new NgoRoomSession(host, room, transport).StartAsync();
                Assert.That(room.IsPublic, Is.False);
                Assert.That((await backend.ListPublicAsync(List())).Count, Is.EqualTo(0));

                int dataChanges = 0;
                room.DataChanged += () => dataChanges++;
                await room.SetPublicAsync(true);
                await room.SetDataAsync("jp.map", "Map_PoC");
                Assert.That(room.IsPublic, Is.True);
                Assert.That(dataChanges, Is.EqualTo(2));
                var rooms = await backend.ListPublicAsync(List());
                Assert.That(rooms.Count, Is.EqualTo(1));
                Assert.That(rooms[0].Id, Is.EqualTo(room.Code.ToString()));
                Assert.That(rooms[0].Name, Is.EqualTo("Host"));
                Assert.That(rooms[0].MemberCount, Is.EqualTo(1));
                Assert.That(rooms[0].MaxMembers, Is.EqualTo(4));
                Assert.That(rooms[0].GetData("jp.map"), Is.EqualTo("Map_PoC"));
                Assert.That((await backend.ListPublicAsync(List("0.2"))).Count, Is.EqualTo(0));

                await room.SetJoinableAsync(false);
                Assert.That((await backend.ListPublicAsync(List())).Count, Is.EqualTo(0), "Closed rooms are not listed.");
                await room.SetJoinableAsync(true);
                await room.SetPublicAsync(false);
                Assert.That((await backend.ListPublicAsync(List())).Count, Is.EqualTo(0));
                Assert.That(Directory.GetFiles(registry), Is.Empty);
            }
            finally
            {
                await NgoTestPeers.DestroyAsync(host);
                DeleteRegistry(registry);
            }
        });

        [UnityTest]
        public IEnumerator ClientFollowsVisibilityAndTheHostEntryTracksMembers() => UniTask.ToCoroutine(async () =>
        {
            string registry = NewRegistry();
            var host = NgoTestPeers.Create("Host");
            var client = NgoTestPeers.Create("Client");
            try
            {
                var hostTransport = NgoTestPeers.Transport(host);
                var hostBackend = new LoopbackRoomBackend(host, hostTransport, registry);
                IRoom hostRoom = await hostBackend.CreateAsync(new RoomCreateOptions { MaxMembers = 4, Name = "Host", Version = "0.1" });
                var hostSession = new NgoRoomSession(host, hostRoom, hostTransport);
                await hostSession.StartAsync();
                await hostRoom.SetPublicAsync(true);

                var clientTransport = NgoTestPeers.Transport(client);
                IRoom clientRoom = await new LoopbackRoomBackend(client, clientTransport, registry).JoinByCodeAsync(hostRoom.Code, default);
                int clientChanges = 0;
                clientRoom.DataChanged += () => clientChanges++;
                await new NgoRoomSession(client, clientRoom, clientTransport).StartAsync().Timeout(TimeSpan.FromSeconds(10));
                await UniTask.WaitUntil(() => clientRoom.IsPublic).Timeout(TimeSpan.FromSeconds(5));
                Assert.That(clientChanges, Is.GreaterThanOrEqualTo(1));
                await UniTask.WaitUntil(() => hostRoom.Members.Count == 2).Timeout(TimeSpan.FromSeconds(5));
                Assert.That((await hostBackend.ListPublicAsync(List()))[0].MemberCount, Is.EqualTo(2));

                await hostRoom.SetPublicAsync(false);
                await UniTask.WaitUntil(() => !clientRoom.IsPublic).Timeout(TimeSpan.FromSeconds(5));

                await hostRoom.SetPublicAsync(true);
                await hostSession.LeaveAsync();
                Assert.That(Directory.GetFiles(registry), Is.Empty, "Leaving removes the entry.");
            }
            finally
            {
                await NgoTestPeers.DestroyAsync(host, client);
                DeleteRegistry(registry);
            }
        });

        [Test]
        public void ExpiredUnreadableFullAndForeignEntriesAreSkipped()
        {
            string registry = NewRegistry();
            var manager = NgoTestPeers.Create("Lister");
            try
            {
                Directory.CreateDirectory(registry);
                void Write(string code, string json, double ageSeconds = 0)
                {
                    string path = Path.Combine(registry, code + ".json");
                    File.WriteAllText(path, json);
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-ageSeconds));
                }
                string Json(string code, string version, int members, int max, bool open) =>
                    "{\"code\":\"" + code + "\",\"name\":\"N\",\"version\":\"" + version + "\",\"members\":" + members +
                    ",\"max\":" + max + ",\"open\":" + (open ? "true" : "false") + ",\"keys\":[],\"values\":[]}";
                Write("ABC234", Json("ABC234", "0.1", 1, 4, true));
                Write("BCD345", Json("BCD345", "0.1", 1, 4, true), 10);
                Write("CDE456", "{");
                Write("DEF567", Json("DEF567", "0.1", 4, 4, true));
                Write("EFG678", Json("EFG678", "0.1", 1, 4, false));
                Write("FGH789", Json("FGH789", "0.2", 1, 4, true));
                Write("GHJ892", Json("GHJ892", "0.1", 1, 0, true));

                var backend = new LoopbackRoomBackend(manager, NgoTestPeers.Transport(manager), registry);
                var rooms = backend.ListPublicAsync(new RoomListOptions { Version = "0.1" }).GetAwaiter().GetResult();
                var ids = new List<string>();
                foreach (var listing in rooms) ids.Add(listing.Id);
                ids.Sort(StringComparer.Ordinal);
                Assert.That(ids, Is.EqualTo(new[] { "ABC234", "GHJ892" }), "Unknown capacity (0) is listed.");
                Assert.That(backend.ListPublicAsync(new RoomListOptions { Version = "0.1", MaxResults = 1 }).GetAwaiter().GetResult().Count, Is.EqualTo(1));
            }
            finally
            {
                Object.Destroy(manager.gameObject);
                DeleteRegistry(registry);
            }
        }
    }
}
