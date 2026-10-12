using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Bun3.Unity.Rooms.Steam.Ngo.Tests
{
    public class SteamRoomTransportTests
    {
        private sealed class MembersRoom : IRoom
        {
            private readonly List<RoomMember> members = new List<RoomMember>();

            public MembersRoom(params string[] ids)
            {
                foreach (string id in ids) members.Add(new RoomMember(id, 0));
            }

            public string Id => "1";
            public RoomCode Code => default;
            public string Name => null;
            public IReadOnlyList<RoomMember> Members => members;
            public int MaxMembers => 0;
            public string LocalMemberId => members[0].Id;
            public string OwnerId => members[0].Id;
            public bool IsOwner => true;
            public bool IsJoinable => true;
            public event Action MembersChanged { add { } remove { } }
            public event Action OwnerChanged { add { } remove { } }
            public event Action DataChanged { add { } remove { } }
            public event Action Closed { add { } remove { } }
            public string GetData(string key) => null;
            public UniTask SetDataAsync(string key, string value) => UniTask.CompletedTask;
            public UniTask SetJoinableAsync(bool joinable) => UniTask.CompletedTask;
            public UniTask TransferOwnershipAsync(string memberId) => UniTask.CompletedTask;
            public UniTask LeaveAsync() => UniTask.CompletedTask;
        }

        [Test]
        public void OnlyCurrentRoomMembersAreAccepted()
        {
            var room = new MembersRoom("76561198000000001", "76561198000000002");
            Assert.That(SteamRoomTransport.IsMember(room, 76561198000000002), Is.True);
            Assert.That(SteamRoomTransport.IsMember(room, 76561198000000003), Is.False);
        }

        [Test]
        public void HostEndpointIsTheHostSteamId() =>
            Assert.That(SteamRoomTransport.ParseEndpoint("76561198000000001"), Is.EqualTo(76561198000000001ul));

        [TestCase("")]
        [TestCase(null)]
        [TestCase("0")]
        [TestCase("127.0.0.1:7777")]
        [TestCase(" 1")]
        public void MalformedEndpointsAreRejected(string endpoint) =>
            Assert.Throws<FormatException>(() => SteamRoomTransport.ParseEndpoint(endpoint));
    }
}
