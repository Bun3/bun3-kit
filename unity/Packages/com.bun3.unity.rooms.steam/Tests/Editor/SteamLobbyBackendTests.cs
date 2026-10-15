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

        private ulong AddPublicLobby(string code, string version, int members, int max, string map)
        {
            ulong lobby = api.AddLobby(Friend, code, version, "900", max);
            var entry = api.Lobbies[lobby];
            for (int i = 1; i < members; i++) entry.Members.Add(76561198000000100UL + (ulong)i);
            entry.Data[SteamLobbyBackend.PublicKey] = "1";
            entry.Data[SteamLobbyBackend.MembersKey] = members.ToString();
            entry.Data[SteamLobbyBackend.MaxKey] = max.ToString();
            entry.Data["jp.map"] = map;
            return lobby;
        }

        [UnityTest]
        public IEnumerator CreatedLobbyStartsPrivateWithListingData() => UniTask.ToCoroutine(async () =>
        {
            var room = await backend.CreateAsync(Create);
            var lobby = api.Lobbies[ulong.Parse(room.Id)];
            Assert.That(lobby.Data[SteamLobbyBackend.PublicKey], Is.EqualTo("0"));
            Assert.That(lobby.Data[SteamLobbyBackend.MaxKey], Is.EqualTo("8"));
            Assert.That(lobby.Data[SteamLobbyBackend.MembersKey], Is.EqualTo("1"));
            Assert.That(((SteamLobbyRoom)room).IsPublic, Is.False);
        });

        [UnityTest]
        public IEnumerator OnlyTheOwnerSwitchesPublicAndMembersSeeIt() => UniTask.ToCoroutine(async () =>
        {
            var owned = (SteamLobbyRoom)await backend.CreateAsync(Create);
            await owned.SetPublicAsync(true);
            Assert.That(api.Lobbies[owned.Lobby].Data[SteamLobbyBackend.PublicKey], Is.EqualTo("1"));
            Assert.That(owned.IsPublic, Is.True);
            await owned.SetPublicAsync(false);
            Assert.That(owned.IsPublic, Is.False);
            await owned.LeaveAsync();

            ulong lobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            var joined = (SteamLobbyRoom)await backend.JoinByIdAsync(lobby.ToString(), Join());
            Assert.Throws<InvalidOperationException>(() => joined.SetPublicAsync(true));
            int dataChanges = 0;
            joined.DataChanged += () => dataChanges++;
            api.Lobbies[lobby].Data[SteamLobbyBackend.PublicKey] = "1";
            api.RaiseDataUpdated(lobby, lobby);
            Assert.That(joined.IsPublic, Is.True);
            Assert.That(dataChanges, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator OwnerKeepsTheListedMemberCountCurrent() => UniTask.ToCoroutine(async () =>
        {
            var room = (SteamLobbyRoom)await backend.CreateAsync(Create);
            var lobby = api.Lobbies[room.Lobby];
            lobby.Members.Add(Friend);
            api.RaiseChatUpdated(room.Lobby, Friend, (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered);
            Assert.That(lobby.Data[SteamLobbyBackend.MembersKey], Is.EqualTo("2"));
            await room.LeaveAsync();

            ulong friendLobby = api.AddLobby(Friend, "ABC234", "0.1", "900");
            api.Lobbies[friendLobby].Data[SteamLobbyBackend.MembersKey] = "1";
            await backend.JoinByIdAsync(friendLobby.ToString(), Join());
            Assert.That(api.Lobbies[friendLobby].Data[SteamLobbyBackend.MembersKey], Is.EqualTo("1"), "Members do not write the count.");
            api.Lobbies[friendLobby].Owner = Me;
            api.RaiseDataUpdated(friendLobby, friendLobby);
            Assert.That(api.Lobbies[friendLobby].Data[SteamLobbyBackend.MembersKey], Is.EqualTo("2"), "A new owner republishes the count.");
        });

        [UnityTest]
        public IEnumerator ListingReturnsPublicSameVersionRoomsWithAFreeSlot() => UniTask.ToCoroutine(async () =>
        {
            ulong listed = AddPublicLobby("ABC234", "0.1", 2, 8, "Map_PoC");
            ulong hidden = api.AddLobby(Friend, "BCD345", "0.1", "900");
            AddPublicLobby("CDE456", "0.2", 1, 8, "Map_PoC");
            AddPublicLobby("DEF567", "0.1", 4, 4, "Map_PoC");
            ulong closed = AddPublicLobby("EFG678", "0.1", 1, 8, "Map_PoC");
            api.Lobbies[closed].Joinable = false;
            ulong garbled = AddPublicLobby("FGH789", "0.1", 1, 8, "Map_PoC");
            api.Lobbies[garbled].Data[SteamLobbyBackend.MembersKey] = "many";
            ulong badCode = AddPublicLobby("GHJ892", "0.1", 1, 8, "Map_PoC");
            api.Lobbies[badCode].Data[SteamLobbyBackend.CodeKey] = "0O1I";

            var rooms = await backend.ListPublicAsync(new RoomListOptions { Version = "0.1", MaxResults = 10, DataKeys = new[] { "jp.map" } });

            Assert.That(rooms.Count, Is.EqualTo(1));
            Assert.That(rooms[0].Id, Is.EqualTo(listed.ToString()));
            Assert.That(rooms[0].Name, Is.EqualTo("Friend"));
            Assert.That(rooms[0].MemberCount, Is.EqualTo(2));
            Assert.That(rooms[0].MaxMembers, Is.EqualTo(8));
            Assert.That(rooms[0].GetData("jp.map"), Is.EqualTo("Map_PoC"));
            Assert.That(api.LastSearchRequiredSlot, Is.True);
            Assert.That(api.LastMaxResults, Is.EqualTo(10));
            Assert.That(api.LastFilters, Does.Contain(new KeyValuePair<string, string>(SteamLobbyBackend.PublicKey, "1")));
            Assert.That(api.LastFilters, Does.Contain(new KeyValuePair<string, string>(SteamLobbyBackend.VersionKey, "0.1")));
            Assert.That(hidden, Is.Not.EqualTo(listed));
        });

        [UnityTest]
        public IEnumerator ListingFailsWhenSteamIsDownAndWorksInsideARoom() => UniTask.ToCoroutine(async () =>
        {
            async UniTask<RoomFailure> ListFailure()
            {
                try { await backend.ListPublicAsync(new RoomListOptions { Version = "0.1" }); }
                catch (RoomException error) { return error.Failure; }
                Assert.Fail("Expected a RoomException.");
                return default;
            }
            api.IsAvailable = false;
            Assert.That(await ListFailure(), Is.EqualTo(RoomFailure.ServiceUnavailable));
            api.IsAvailable = true;
            api.FailSearch = true;
            Assert.That(await ListFailure(), Is.EqualTo(RoomFailure.ServiceUnavailable));
            api.FailSearch = false;
            var room = (SteamLobbyRoom)await backend.CreateAsync(Create);
            await room.SetPublicAsync(true);
            var rooms = await backend.ListPublicAsync(new RoomListOptions { Version = "0.1" });
            Assert.That(rooms.Count, Is.EqualTo(1), "The owner's own room may appear; filtering it is the game's job.");
            Assert.That(api.LastMaxResults, Is.EqualTo(RoomListOptions.Limit));
        });
    }
}
