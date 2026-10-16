using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace Bun3.Unity.Rooms.Ngo
{
    /// <summary>
    /// Rooms for processes on one machine (Editor, Multiplayer Play Mode) without a platform service.
    /// The room code encodes the host's UDP port, so joining needs no lookup, and the room id is the code.
    /// Room data lives only in the local process. The host sees every NGO connection as a member
    /// (<c>local-{clientId}</c>); a client sees the host and itself. Capacity and the lock are known only to the
    /// host; a client reports a capacity of 0 and a joinable room. No migration, invites or version checks:
    /// a client's room closes when its NGO client stops.
    /// Public rooms are listed through a registry directory shared by processes of the same product on this machine:
    /// the host rewrites <c>{code}.json</c> every 2 seconds, lists skip entries older than 6 seconds, and a client
    /// learns <see cref="IRoom.IsPublic"/> by checking its room's entry every 2 seconds.
    /// </summary>
    public sealed class LoopbackRoomBackend : IRoomBackend
    {
        private const string HostMemberId = "local-0";
        private const int HeartbeatMs = 2000;
        private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(6);
        private readonly NetworkManager manager;
        private readonly UtpRoomTransport transport;
        private readonly string registry;
        private LoopbackRoom current;

        /// <summary>
        /// Creates a backend; the code of a created room is the port set on <paramref name="transport"/>.
        /// <paramref name="registryDirectory"/> defaults to <c>Application.temporaryCachePath/bun3-rooms</c>.
        /// </summary>
        public LoopbackRoomBackend(NetworkManager manager, UtpRoomTransport transport, string registryDirectory = null)
        {
            this.manager = manager != null ? manager : throw new ArgumentNullException(nameof(manager));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            registry = registryDirectory ?? Path.Combine(Application.temporaryCachePath, "bun3-rooms");
        }

        /// <summary>Never raised: loopback rooms have no invites.</summary>
        public event Action<string> JoinRequested { add { } remove { } }

        /// <inheritdoc/>
        public bool TryOpenInviteOverlay(IRoom room) => false;

        /// <summary>Reserves a free UDP port as <see cref="UtpRoomTransport.ListenPort"/>; the code is that port.</summary>
        public UniTask<IRoom> CreateAsync(RoomCreateOptions options, CancellationToken cancellationToken = default)
        {
            EnsureNoRoom();
            ushort port = UtpRoomTransport.FreeUdpPort();
            transport.ListenPort = port;
            current = new LoopbackRoom(this, manager, RoomCode.FromValue(port), options.Name, options.Version, options.MaxMembers, true);
            return UniTask.FromResult<IRoom>(current);
        }

        /// <summary>Builds a room pointing at <c>127.0.0.1:{code value}</c>. Codes outside the port range are not found.</summary>
        public UniTask<IRoom> JoinByCodeAsync(RoomCode code, RoomJoinOptions options, CancellationToken cancellationToken = default)
        {
            EnsureNoRoom();
            if (code.Value == 0 || code.Value > ushort.MaxValue) throw new RoomException(RoomFailure.NotFound);
            var room = new LoopbackRoom(this, manager, code, null, null, 0, false);
            room.Preset(NgoRoomSession.HostMemberKey, HostMemberId);
            room.Preset(NgoRoomSession.HostKey, string.Concat("127.0.0.1:", code.Value.ToString(CultureInfo.InvariantCulture)));
            room.Preset(NgoRoomSession.HostEpochKey, "1");
            current = room;
            return UniTask.FromResult<IRoom>(room);
        }

        /// <summary>Loopback room ids are their codes.</summary>
        public UniTask<IRoom> JoinByIdAsync(string roomId, RoomJoinOptions options, CancellationToken cancellationToken = default) =>
            RoomCode.TryNormalize(roomId, out var code)
                ? JoinByCodeAsync(code, options, cancellationToken)
                : throw new RoomException(RoomFailure.NotFound);

        /// <summary>Reads the registry. Unreadable, expired, closed, full and other-version entries are skipped.</summary>
        public UniTask<IReadOnlyList<RoomListing>> ListPublicAsync(RoomListOptions options, CancellationToken cancellationToken = default)
        {
            var rooms = new List<RoomListing>();
            if (!Directory.Exists(registry)) return UniTask.FromResult<IReadOnlyList<RoomListing>>(rooms);
            DateTime now = DateTime.UtcNow;
            foreach (string path in Directory.GetFiles(registry, "*.json"))
            {
                if (rooms.Count >= options.Count) break;
                Entry entry;
                try
                {
                    if (now - File.GetLastWriteTimeUtc(path) > Expiry) continue;
                    entry = JsonUtility.FromJson<Entry>(File.ReadAllText(path));
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
                {
                    continue;
                }
                if (entry == null || !entry.open || !RoomCode.TryNormalize(entry.code, out _) ||
                    !string.Equals(entry.version ?? string.Empty, options.Version ?? string.Empty, StringComparison.Ordinal) ||
                    (entry.max > 0 && entry.members >= entry.max))
                    continue;
                rooms.Add(new RoomListing(entry.code, string.IsNullOrEmpty(entry.name) ? null : entry.name, entry.members, entry.max,
                    Requested(entry, options.DataKeys)));
            }
            return UniTask.FromResult<IReadOnlyList<RoomListing>>(rooms);
        }

        private static Dictionary<string, string> Requested(Entry entry, IReadOnlyList<string> keys)
        {
            if (keys == null || entry.keys == null || entry.values == null) return null;
            var data = new Dictionary<string, string>(keys.Count, StringComparer.Ordinal);
            for (int i = 0; i < entry.keys.Length && i < entry.values.Length; i++)
                for (int k = 0; k < keys.Count; k++)
                    if (entry.keys[i] == keys[k]) data[keys[k]] = entry.values[i];
            return data;
        }

        private string EntryPath(string code) => Path.Combine(registry, string.Concat(code, ".json"));

        private bool IsListed(string code)
        {
            try
            {
                string path = EntryPath(code);
                return File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <= Expiry;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void EnsureNoRoom()
        {
            if (current != null) throw new InvalidOperationException("Leave the current room first.");
        }

        [Serializable]
        private sealed class Entry
        {
            public string code;
            public string name;
            public string version;
            public int members;
            public int max;
            public bool open;
            public string[] keys;
            public string[] values;
        }

        private sealed class LoopbackRoom : IRoom
        {
            private readonly LoopbackRoomBackend backend;
            private readonly NetworkManager manager;
            private readonly string version;
            private readonly Dictionary<string, string> data = new Dictionary<string, string>();
            private readonly Dictionary<ulong, string> memberIds = new Dictionary<ulong, string>();
            private readonly List<RoomMember> members = new List<RoomMember>();
            private CancellationTokenSource timer;
            private bool released;

            public LoopbackRoom(LoopbackRoomBackend backend, NetworkManager manager, RoomCode code, string name, string version, int maxMembers, bool owner)
            {
                this.backend = backend;
                this.manager = manager;
                this.version = version;
                Code = code;
                Name = name;
                MaxMembers = maxMembers;
                IsOwner = owner;
                Id = code.ToString();
                manager.OnConnectionEvent += OnConnectionEvent;
                manager.OnClientStopped += OnClientStopped;
                if (!owner) StartTimer(PollAsync);
            }

            public string Id { get; }
            public RoomCode Code { get; }
            public string Name { get; }
            public bool IsOwner { get; }
            public int MaxMembers { get; }
            public bool IsJoinable { get; private set; } = true;
            public bool IsPublic { get; private set; }
            public string OwnerId => HostMemberId;

            public string LocalMemberId =>
                manager.IsListening && (manager.IsServer || manager.IsConnectedClient) ? MemberId(manager.LocalClientId) : null;

            public IReadOnlyList<RoomMember> Members
            {
                get
                {
                    members.Clear();
                    if (manager.IsServer)
                    {
                        var ids = manager.ConnectedClientsIds;
                        for (int i = 0; i < ids.Count; i++)
                            members.Add(new RoomMember(MemberId(ids[i]), (long)ids[i]));
                    }
                    else if (manager.IsConnectedClient)
                    {
                        members.Add(new RoomMember(HostMemberId, 0));
                        members.Add(new RoomMember(MemberId(manager.LocalClientId), (long)manager.LocalClientId));
                    }
                    return members;
                }
            }

            public event Action MembersChanged;
            public event Action OwnerChanged { add { } remove { } }
            public event Action DataChanged;
            public event Action Closed;

            public string GetData(string key) => data.TryGetValue(key, out string value) ? value : null;

            public UniTask SetDataAsync(string key, string value)
            {
                RequireOwner();
                data[key] = value;
                WriteEntry();
                DataChanged?.Invoke();
                return UniTask.CompletedTask;
            }

            // Only the host process sees the lock; admission stays the game's job.
            public UniTask SetJoinableAsync(bool joinable)
            {
                RequireOwner();
                IsJoinable = joinable;
                WriteEntry();
                DataChanged?.Invoke();
                return UniTask.CompletedTask;
            }

            public UniTask SetPublicAsync(bool isPublic)
            {
                RequireOwner();
                if (IsPublic == isPublic) return UniTask.CompletedTask;
                IsPublic = isPublic;
                if (isPublic)
                {
                    WriteEntry();
                    StartTimer(HeartbeatAsync);
                }
                else
                {
                    StopTimer();
                    DeleteEntry();
                }
                DataChanged?.Invoke();
                return UniTask.CompletedTask;
            }

            // Ownership is fixed; a client's room closes when its host leaves.
            public UniTask TransferOwnershipAsync(string memberId)
            {
                RequireOwner();
                return UniTask.CompletedTask;
            }

            public UniTask LeaveAsync()
            {
                Release();
                return UniTask.CompletedTask;
            }

            public void Preset(string key, string value) => data[key] = value;

            private void OnConnectionEvent(NetworkManager source, ConnectionEventData eventData)
            {
                if (!manager.IsServer) return;
                WriteEntry();
                MembersChanged?.Invoke();
            }

            private void OnClientStopped(bool wasHost)
            {
                if (IsOwner || released) return;
                Release();
                Closed?.Invoke();
            }

            private void Release()
            {
                if (released) return;
                released = true;
                StopTimer();
                if (IsOwner && IsPublic) DeleteEntry();
                manager.OnConnectionEvent -= OnConnectionEvent;
                manager.OnClientStopped -= OnClientStopped;
                if (backend.current == this) backend.current = null;
            }

            private void StartTimer(Func<CancellationToken, UniTaskVoid> loop)
            {
                StopTimer();
                timer = new CancellationTokenSource();
                loop(timer.Token).Forget();
            }

            private void StopTimer()
            {
                if (timer == null) return;
                timer.Cancel();
                timer.Dispose();
                timer = null;
            }

            private async UniTaskVoid HeartbeatAsync(CancellationToken token)
            {
                while (!await UniTask.Delay(HeartbeatMs, DelayType.Realtime, cancellationToken: token).SuppressCancellationThrow())
                    WriteEntry();
            }

            private async UniTaskVoid PollAsync(CancellationToken token)
            {
                while (!await UniTask.Delay(HeartbeatMs, DelayType.Realtime, cancellationToken: token).SuppressCancellationThrow())
                {
                    bool listed = backend.IsListed(Id);
                    if (listed == IsPublic) continue;
                    IsPublic = listed;
                    DataChanged?.Invoke();
                }
            }

            private void WriteEntry()
            {
                if (!IsOwner || !IsPublic || released) return;
                var keys = new string[data.Count];
                var values = new string[data.Count];
                int i = 0;
                foreach (var pair in data)
                {
                    keys[i] = pair.Key;
                    values[i++] = pair.Value;
                }
                var entry = new Entry
                {
                    code = Id, name = Name, version = version, members = Members.Count, max = MaxMembers, open = IsJoinable,
                    keys = keys, values = values,
                };
                try
                {
                    Directory.CreateDirectory(backend.registry);
                    File.WriteAllText(backend.EntryPath(Id), JsonUtility.ToJson(entry));
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    // The next heartbeat retries.
                }
            }

            private void DeleteEntry()
            {
                try { File.Delete(backend.EntryPath(Id)); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            }

            private void RequireOwner()
            {
                if (!IsOwner) throw new InvalidOperationException("Only the room owner can change a loopback room.");
            }

            private string MemberId(ulong clientId)
            {
                if (!memberIds.TryGetValue(clientId, out string id))
                {
                    id = string.Concat("local-", clientId.ToString(CultureInfo.InvariantCulture));
                    memberIds[clientId] = id;
                }
                return id;
            }
        }
    }
}
