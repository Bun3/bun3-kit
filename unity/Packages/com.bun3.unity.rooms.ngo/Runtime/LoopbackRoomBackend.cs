using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;

namespace Bun3.Unity.Rooms.Ngo
{
    /// <summary>
    /// Rooms for processes on one machine (Editor, Multiplayer Play Mode) without a platform service.
    /// The room code encodes the host's UDP port, so joining needs no lookup, and the room id is the code.
    /// Room data lives only in the local process. The host sees every NGO connection as a member
    /// (<c>local-{clientId}</c>); a client sees the host and itself. Capacity and the lock are known only to the
    /// host; a client reports a capacity of 0 and a joinable room. No migration, invites or version checks:
    /// a client's room closes when its NGO client stops.
    /// </summary>
    public sealed class LoopbackRoomBackend : IRoomBackend
    {
        private const string HostMemberId = "local-0";
        private readonly NetworkManager manager;
        private readonly UtpRoomTransport transport;
        private LoopbackRoom current;

        /// <summary>Creates a backend; the code of a created room is the port set on <paramref name="transport"/>.</summary>
        public LoopbackRoomBackend(NetworkManager manager, UtpRoomTransport transport)
        {
            this.manager = manager != null ? manager : throw new ArgumentNullException(nameof(manager));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
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
            current = new LoopbackRoom(this, manager, RoomCode.FromValue(port), options.Name, options.MaxMembers, true);
            return UniTask.FromResult<IRoom>(current);
        }

        /// <summary>Builds a room pointing at <c>127.0.0.1:{code value}</c>. Codes outside the port range are not found.</summary>
        public UniTask<IRoom> JoinByCodeAsync(RoomCode code, RoomJoinOptions options, CancellationToken cancellationToken = default)
        {
            EnsureNoRoom();
            if (code.Value == 0 || code.Value > ushort.MaxValue) throw new RoomException(RoomFailure.NotFound);
            var room = new LoopbackRoom(this, manager, code, null, 0, false);
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

        private void EnsureNoRoom()
        {
            if (current != null) throw new InvalidOperationException("Leave the current room first.");
        }

        private sealed class LoopbackRoom : IRoom
        {
            private readonly LoopbackRoomBackend backend;
            private readonly NetworkManager manager;
            private readonly Dictionary<string, string> data = new Dictionary<string, string>();
            private readonly Dictionary<ulong, string> memberIds = new Dictionary<ulong, string>();
            private readonly List<RoomMember> members = new List<RoomMember>();
            private bool released;

            public LoopbackRoom(LoopbackRoomBackend backend, NetworkManager manager, RoomCode code, string name, int maxMembers, bool owner)
            {
                this.backend = backend;
                this.manager = manager;
                Code = code;
                Name = name;
                MaxMembers = maxMembers;
                IsOwner = owner;
                Id = code.ToString();
                manager.OnConnectionEvent += OnConnectionEvent;
                manager.OnClientStopped += OnClientStopped;
            }

            public string Id { get; }
            public RoomCode Code { get; }
            public string Name { get; }
            public bool IsOwner { get; }
            public int MaxMembers { get; }
            public bool IsJoinable { get; private set; } = true;
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
                DataChanged?.Invoke();
                return UniTask.CompletedTask;
            }

            // Only the host process sees the lock; admission stays the game's job.
            public UniTask SetJoinableAsync(bool joinable)
            {
                RequireOwner();
                IsJoinable = joinable;
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
                if (manager.IsServer) MembersChanged?.Invoke();
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
                manager.OnConnectionEvent -= OnConnectionEvent;
                manager.OnClientStopped -= OnClientStopped;
                if (backend.current == this) backend.current = null;
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
