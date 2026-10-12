using System;
using System.Collections;
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
    }
}
