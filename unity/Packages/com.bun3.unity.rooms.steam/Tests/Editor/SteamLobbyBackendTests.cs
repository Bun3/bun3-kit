using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Steamworks;
using UnityEngine.TestTools;

namespace Bun3.Unity.Rooms.Steam.Tests
{
    public class SteamLobbyBackendTests
    {
        private const ulong Me = 76561198000000001;
        private const ulong Friend = 76561198000000002;
        private FakeSteamLobbyApi api;
        private SteamLobbyBackend backend;

        [SetUp]
        public void SetUp()
        {
            api = new FakeSteamLobbyApi { LocalUserId = Me };
            backend = new SteamLobbyBackend(api);
        }

        [TearDown]
        public void TearDown() => backend.Dispose();

        private static RoomCreateOptions Create => new RoomCreateOptions { MaxMembers = 8, Name = "Host", Version = "0.1" };

        private static RoomJoinOptions Join(string version = "0.1") => new RoomJoinOptions { Version = version };

        private static RoomCode Code(string text)
        {
            Assert.That(RoomCode.TryNormalize(text, out var code), Is.True);
            return code;
        }

        private static string[] Ids(IReadOnlyList<RoomMember> members)
        {
            var ids = new string[members.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = members[i].Id;
            return ids;
        }

        private static async UniTask<RoomFailure> FailureAsync(Func<UniTask<IRoom>> call)
        {
            try { await call(); }
            catch (RoomException error) { return error.Failure; }
            Assert.Fail("Expected a RoomException.");
            return default;
        }

        [UnityTest]
        public IEnumerator CreatedLobbyPublishesCodeVersionNameAndInvite() => UniTask.ToCoroutine(async () =>
        {
            var room = await backend.CreateAsync(Create);
            ulong id = ulong.Parse(room.Id);
            var lobby = api.Lobbies[id];
            Assert.That(lobby.Data[SteamLobbyBackend.CodeKey], Is.EqualTo(room.Code.ToString()));
            Assert.That(lobby.Data[SteamLobbyBackend.VersionKey], Is.EqualTo("0.1"));
            Assert.That(room.Name, Is.EqualTo("Host"));
            Assert.That(lobby.MaxMembers, Is.EqualTo(8));
            Assert.That(room.MaxMembers, Is.EqualTo(8));
            Assert.That(room.IsJoinable, Is.True);
            Assert.That(room.IsOwner, Is.True);
            Assert.That(room.LocalMemberId, Is.EqualTo(Me.ToString()));
            Assert.That(lobby.MemberData[(Me, SteamLobbyBackend.JoinedKey)], Is.EqualTo("1000"));
            Assert.That(api.RichPresence["connect"], Is.EqualTo("+connect_lobby " + room.Id));
            Assert.That(api.LastSearchRequiredSlot, Is.False);
            Assert.That(backend.TryOpenInviteOverlay(room), Is.True);
            Assert.That(api.InviteDialogLobby, Is.EqualTo(id));
        });

        [UnityTest]
        public IEnumerator CodeCollisionsRetryThenGiveUp() => UniTask.ToCoroutine(async () =>
        {
            api.CollisionsRemaining = 2;
            var room = await backend.CreateAsync(Create);
            Assert.That(api.Searches, Is.EqualTo(3));
            await room.LeaveAsync();

            api.CollisionsRemaining = 5;
            api.Searches = 0;
            Assert.That(await FailureAsync(() => backend.CreateAsync(Create)), Is.EqualTo(RoomFailure.ServiceUnavailable));
            Assert.That(api.Searches, Is.EqualTo(5));
        });

        [UnityTest]
        public IEnumerator JoiningByCodeEntersTheMatchingLobby() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            var room = await backend.JoinByCodeAsync(Code("abc234"), Join());
            Assert.That(room.Id, Is.EqualTo(lobby.ToString()));
            Assert.That(api.LastSearchRequiredSlot, Is.True);
            Assert.That(room.IsOwner, Is.False);
            Assert.That(room.OwnerId, Is.EqualTo(Friend.ToString()));
            Assert.That(room.Name, Is.EqualTo("Friend"));
            Assert.That(Ids(room.Members), Is.EqualTo(new[] { Friend.ToString(), Me.ToString() }));
            Assert.That(RoomOwnerElection.Oldest(room.Members, null), Is.EqualTo(Friend.ToString()));
        });

        [UnityTest]
        public IEnumerator JoinFailuresMapToRoomFailures() => UniTask.ToCoroutine(async () =>
        {
            Assert.That(await FailureAsync(() => backend.JoinByCodeAsync(Code("ABC234"), Join())), Is.EqualTo(RoomFailure.NotFound));

            ulong lobby = api.AddLobby(Friend, "ABC234", "0.2", "900");
            Assert.That(await FailureAsync(() => backend.JoinByCodeAsync(Code("ABC234"), Join("0.1"))), Is.EqualTo(RoomFailure.VersionMismatch));
            Assert.That(api.Left, Does.Contain(lobby));

            api.JoinResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseFull;
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync(lobby.ToString(), Join("0.2"))), Is.EqualTo(RoomFailure.Full));
            api.JoinResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited;
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync(lobby.ToString(), Join("0.2"))), Is.EqualTo(RoomFailure.NotFound));
            api.JoinResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseError;
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync(lobby.ToString(), Join("0.2"))), Is.EqualTo(RoomFailure.ServiceUnavailable));
            api.JoinResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync("not-a-lobby", Join())), Is.EqualTo(RoomFailure.NotFound));

            var room = await backend.JoinByIdAsync(lobby.ToString(), Join("0.2"));
            Assert.That(room.Id, Is.EqualTo(lobby.ToString()), "Failed joins must not leave the backend holding a room.");
        });

        [UnityTest]
        public IEnumerator LobbyUpdatesRaiseOnlyRealChanges() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            var room = await backend.JoinByIdAsync(lobby.ToString(), Join());
            int ownerChanges = 0, memberChanges = 0;
            room.OwnerChanged += () => ownerChanges++;
            room.MembersChanged += () => memberChanges++;

            api.Lobbies[lobby].Owner = Me;
            api.RaiseDataUpdated(lobby, lobby);
            Assert.That(ownerChanges, Is.EqualTo(1));
            Assert.That(room.IsOwner, Is.True);
            api.RaiseDataUpdated(lobby, lobby);
            Assert.That(ownerChanges, Is.EqualTo(1), "Unchanged data must not repeat the event.");
            Assert.That(memberChanges, Is.EqualTo(0));

            api.Lobbies[lobby].Members.Remove(Friend);
            api.RaiseChatUpdated(lobby, Friend, (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft);
            Assert.That(memberChanges, Is.EqualTo(1));
            Assert.That(Ids(room.Members), Is.EqualTo(new[] { Me.ToString() }));
        });

        [UnityTest]
        public IEnumerator LockingHidesTheRoomAndTheInvite() => UniTask.ToCoroutine(async () =>
        {
            var room = await backend.CreateAsync(Create);
            await room.SetJoinableAsync(false);
            var lobby = api.Lobbies[ulong.Parse(room.Id)];
            Assert.That(lobby.Joinable, Is.False);
            Assert.That(lobby.Data[SteamLobbyBackend.OpenKey], Is.EqualTo("0"), "Members learn about the lock from lobby data.");
            Assert.That(room.IsJoinable, Is.False);
            Assert.That(api.RichPresence.ContainsKey("connect"), Is.False);

            var other = new SteamLobbyBackend(api);
            Assert.That(await FailureAsync(() => other.JoinByCodeAsync(room.Code, Join())), Is.EqualTo(RoomFailure.NotFound));
            other.Dispose();

            await room.SetJoinableAsync(true);
            Assert.That(room.IsJoinable, Is.True);
            Assert.That(lobby.Data[SteamLobbyBackend.OpenKey], Is.EqualTo("1"));
            Assert.That(api.RichPresence["connect"], Is.EqualTo("+connect_lobby " + room.Id));
        });

        [UnityTest]
        public IEnumerator MembersFollowTheLockAndDataChangesComeFromTheLobby() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900", maxMembers: 6);
            var room = await backend.JoinByIdAsync(lobby.ToString(), Join());
            int dataChanges = 0;
            room.DataChanged += () => dataChanges++;
            Assert.That(room.MaxMembers, Is.EqualTo(6));
            Assert.That(room.IsJoinable, Is.True);
            Assert.That(api.RichPresence["connect"], Is.EqualTo("+connect_lobby " + room.Id));

            api.Lobbies[lobby].Data[SteamLobbyBackend.OpenKey] = "0";
            api.RaiseDataUpdated(lobby, lobby);
            Assert.That(room.IsJoinable, Is.False);
            Assert.That(api.RichPresence.ContainsKey("connect"), Is.False, "Friends of members must not see Join Game while locked.");
            Assert.That(dataChanges, Is.EqualTo(1));

            api.RaiseDataUpdated(lobby, Friend);
            Assert.That(dataChanges, Is.EqualTo(1), "Member data changes are not room data changes.");

            api.Lobbies[lobby].Data[SteamLobbyBackend.OpenKey] = "1";
            api.RaiseDataUpdated(lobby, lobby);
            Assert.That(room.IsJoinable, Is.True);
            Assert.That(api.RichPresence["connect"], Is.EqualTo("+connect_lobby " + room.Id));
            Assert.That(dataChanges, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator KickClosesTheRoomAndFreesTheBackend() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            var room = await backend.JoinByIdAsync(lobby.ToString(), Join());
            bool closed = false;
            room.Closed += () => closed = true;
            api.RaiseKicked(lobby);
            Assert.That(closed, Is.True);
            Assert.That(api.RichPresence.ContainsKey("connect"), Is.False);
            Assert.That(api.Left, Does.Contain(lobby));
            Assert.That(await backend.CreateAsync(Create), Is.Not.Null);
        });

        [UnityTest]
        public IEnumerator AbandonedJoinIsLeftWhenSteamLateEntersIt() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            api.JoinTimesOut = true;
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync(lobby.ToString(), Join())), Is.EqualTo(RoomFailure.ServiceUnavailable));
            api.JoinTimesOut = false;
            api.RaiseLobbyEntered(lobby);
            Assert.That(api.Left, Does.Contain(lobby));
            Assert.That(await backend.CreateAsync(Create), Is.Not.Null);
        });

        [UnityTest]
        public IEnumerator EntersDuringNormalCallsAreNotLeft() => UniTask.ToCoroutine(async () =>
        {
            var created = await backend.CreateAsync(Create);
            Assert.That(api.Left, Is.Empty);
            await created.LeaveAsync();
            api.Left.Clear();
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            var joined = await backend.JoinByIdAsync(lobby.ToString(), Join());
            Assert.That(api.Left, Is.Empty);
            api.RaiseLobbyEntered(lobby);
            Assert.That(api.Left, Is.Empty, "The current room's lobby is never left.");
            Assert.That(joined.Id, Is.EqualTo(lobby.ToString()));
        });

        [UnityTest]
        public IEnumerator OverlappingOperationsAreRejected() => UniTask.ToCoroutine(async () =>
        {
            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            api.JoinGate = new UniTaskCompletionSource<uint>();
            var first = backend.JoinByIdAsync(lobby.ToString(), Join());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await backend.CreateAsync(Create));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await backend.JoinByIdAsync(lobby.ToString(), Join()));
            api.Lobbies[lobby].Members.Add(Me);
            api.JoinGate.TrySetResult((uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess);
            Assert.That((await first).Id, Is.EqualTo(lobby.ToString()));
        });

        [Test]
        public void OverlayAndJoinGameRequestsReachJoinRequested()
        {
            var ids = new List<string>();
            backend.JoinRequested += ids.Add;
            api.RaiseLobbyJoinRequested(123);
            api.RaiseRichPresenceJoinRequested("+connect_lobby 456");
            api.RaiseRichPresenceJoinRequested("something else");
            Assert.That(ids, Is.EqualTo(new[] { "123", "456" }));
        }

        [TestCase("game.exe +connect_lobby 109775240000000001", "109775240000000001")]
        [TestCase("+connect_lobby   42 -other", "42")]
        public void ConnectCommandYieldsTheLobbyId(string text, string expected)
        {
            Assert.That(SteamLobbyBackend.TryParseConnect(text, out string lobby), Is.True);
            Assert.That(lobby, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("game.exe")]
        [TestCase("+connect_lobby")]
        [TestCase("+connect_lobby 0")]
        [TestCase("+connect_lobby abc")]
        [TestCase("+connect_lobby 99999999999999999999999")]
        public void OtherTextHasNoLobby(string text) =>
            Assert.That(SteamLobbyBackend.TryParseConnect(text, out _), Is.False);

        [UnityTest]
        public IEnumerator UnavailableSteamAndSecondRoomAreRejected() => UniTask.ToCoroutine(async () =>
        {
            api.IsAvailable = false;
            Assert.That(await FailureAsync(() => backend.CreateAsync(Create)), Is.EqualTo(RoomFailure.ServiceUnavailable));
            Assert.That(await FailureAsync(() => backend.JoinByIdAsync("1", Join())), Is.EqualTo(RoomFailure.ServiceUnavailable));
            api.IsAvailable = true;
            await backend.CreateAsync(Create);
            try
            {
                await backend.CreateAsync(Create);
                Assert.Fail("A backend holds one room at a time.");
            }
            catch (InvalidOperationException)
            {
            }
        });
    }
}
