using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace Bun3.Unity.Rooms.Ngo
{
    /// <summary>Lifecycle of an <see cref="NgoRoomSession"/>.</summary>
    public enum RoomSessionState
    {
        /// <summary>Created; <see cref="NgoRoomSession.StartAsync"/> not called yet.</summary>
        Idle,

        /// <summary>Starting NGO for the first time.</summary>
        Starting,

        /// <summary>NGO runs as host or connected client.</summary>
        Connected,

        /// <summary>The NGO host was lost and a replacement is being started.</summary>
        Migrating,

        /// <summary>Migration did not finish in time. NGO is stopped; the room is still joined.</summary>
        Failed,

        /// <summary>NGO is stopped for good: left, room closed, or migration not allowed.</summary>
        Closed,
    }

    /// <summary>Policy hooks for <see cref="NgoRoomSession"/>.</summary>
    public sealed class NgoRoomSessionOptions
    {
        /// <summary>Whether the room survives losing its NGO host right now. Default: never.</summary>
        public Func<bool> CanMigrate { get; set; } = () => false;

        /// <summary>Time allowed to reach <see cref="RoomSessionState.Connected"/> after starting or after losing the host.</summary>
        public TimeSpan MigrationTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Picks the next host from the members, given the departed host's id.</summary>
        public Func<IReadOnlyList<RoomMember>, string, string> ElectHost { get; set; } = RoomOwnerElection.Oldest;
    }

    /// <summary>
    /// Runs NGO for a room. The owner hosts and publishes its endpoint in room data; members connect to it.
    /// When the host is lost and <see cref="NgoRoomSessionOptions.CanMigrate"/> allows it, the elected member
    /// takes ownership and hosts, and the others reconnect. Restarting NGO destroys spawned NetworkObjects:
    /// restore game state in <see cref="HostMigrated"/>. Set connection data such as
    /// <c>NetworkConfig.ConnectionData</c> before <see cref="StartAsync"/>. Main thread only.
    /// </summary>
    public sealed class NgoRoomSession
    {
        /// <summary>Room data key holding the NGO host endpoint.</summary>
        public const string HostKey = "bun3.host";

        /// <summary>Room data key holding the host generation; it grows by one each time a host starts.</summary>
        public const string HostEpochKey = "bun3.hostEpoch";

        /// <summary>Room data key holding the member id of the NGO host.</summary>
        public const string HostMemberKey = "bun3.hostMember";

        private readonly NetworkManager manager;
        private readonly INgoRoomTransport transport;
        private readonly NgoRoomSessionOptions options;
        private string hostMember;
        private int epoch;
        private bool clientStopped;
        private bool checkScheduled;
        private bool left;

        /// <summary>Creates a session; nothing starts until <see cref="StartAsync"/>.</summary>
        public NgoRoomSession(NetworkManager manager, IRoom room, INgoRoomTransport transport, NgoRoomSessionOptions options = null)
        {
            this.manager = manager != null ? manager : throw new ArgumentNullException(nameof(manager));
            Room = room ?? throw new ArgumentNullException(nameof(room));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.options = options ?? new NgoRoomSessionOptions();
        }

        /// <summary>The room this session runs NGO for.</summary>
        public IRoom Room { get; }

        /// <summary>Current lifecycle state.</summary>
        public RoomSessionState State { get; private set; }

        /// <summary>Raised whenever <see cref="State"/> changes.</summary>
        public event Action<RoomSessionState> StateChanged;

        /// <summary>Raised after NGO reconnects under a new host. Spawned objects from before are gone.</summary>
        public event Action HostMigrated;

        /// <summary>
        /// Hosts when the local member owns the room, otherwise waits for the host endpoint and connects.
        /// Completes once <see cref="State"/> is <see cref="RoomSessionState.Connected"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">NGO did not start or connect; the session is closed and the room is still joined.</exception>
        public async UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            if (State != RoomSessionState.Idle) throw new InvalidOperationException("The session has already started.");
            SetState(RoomSessionState.Starting);
            Subscribe();
            double deadline = Deadline();
            try
            {
                bool connected;
                if (Room.IsOwner)
                {
                    await StartHostAsync(1);
                    connected = true;
                }
                else
                {
                    connected = await WaitAsync(() => ReadEpoch() > 0, deadline, cancellationToken) &&
                        await ConnectClientAsync(deadline, cancellationToken);
                }
                if (!connected || State != RoomSessionState.Starting)
                    throw new InvalidOperationException("The room's NGO host could not be reached.");
                SetState(RoomSessionState.Connected);
            }
            catch
            {
                Close();
                await StopNgoAsync();
                throw;
            }
        }

        /// <summary>
        /// Stops NGO and leaves the room. An owner first hands the room to the elected member when
        /// <see cref="NgoRoomSessionOptions.CanMigrate"/> allows it; hand-off failures never block leaving. Idempotent.
        /// </summary>
        public async UniTask LeaveAsync()
        {
            if (left) return;
            left = true;
            bool handOff = Room.IsOwner && Room.Members.Count > 1 && options.CanMigrate();
            Close();
            if (handOff)
            {
                string next = options.ElectHost(Room.Members, Room.LocalMemberId);
                if (next != null)
                {
                    try { await Room.TransferOwnershipAsync(next); }
                    catch (Exception error) { Debug.LogException(error); }
                }
            }
            await Room.LeaveAsync();
            await StopNgoAsync();
        }

        private async UniTask StartHostAsync(int newEpoch)
        {
            string endpoint = transport.PrepareHost(manager, Room);
            if (!manager.StartHost()) throw new InvalidOperationException("NetworkManager.StartHost failed.");
            hostMember = Room.LocalMemberId;
            epoch = newEpoch;
            await Room.SetDataAsync(HostMemberKey, hostMember);
            await Room.SetDataAsync(HostKey, endpoint);
            await Room.SetDataAsync(HostEpochKey, newEpoch.ToString(CultureInfo.InvariantCulture));
        }

        private async UniTask<bool> ConnectClientAsync(double deadline, CancellationToken cancellationToken)
        {
            hostMember = Room.GetData(HostMemberKey);
            epoch = ReadEpoch();
            transport.PrepareClient(manager, Room, Room.GetData(HostKey));
            clientStopped = false;
            if (!manager.StartClient()) return false;
            bool connected = await WaitAsync(() => manager.IsConnectedClient || clientStopped, deadline, cancellationToken) &&
                manager.IsConnectedClient;
            if (!connected) await StopNgoAsync();
            return connected;
        }

        private void Subscribe()
        {
            manager.OnClientStopped += OnClientStopped;
            Room.MembersChanged += ScheduleHostCheck;
            Room.Closed += OnRoomClosed;
        }

        private void Unsubscribe()
        {
            manager.OnClientStopped -= OnClientStopped;
            Room.MembersChanged -= ScheduleHostCheck;
            Room.Closed -= OnRoomClosed;
        }

        private void OnClientStopped(bool wasHost)
        {
            clientStopped = true;
            ScheduleHostCheck();
        }

        private void OnRoomClosed()
        {
            Close();
            StopNgoAsync().Forget();
        }

        // Deferred one frame so a backend without migration can report its own closure first.
        private void ScheduleHostCheck()
        {
            if (checkScheduled || State != RoomSessionState.Connected) return;
            checkScheduled = true;
            CheckHostAsync().Forget();
        }

        private async UniTaskVoid CheckHostAsync()
        {
            await UniTask.Yield();
            checkScheduled = false;
            if (State != RoomSessionState.Connected || manager == null || manager.IsHost) return;
            if (manager.IsConnectedClient && IsMember(hostMember)) return;
            if (!options.CanMigrate())
            {
                Close();
                await StopNgoAsync();
                return;
            }
            await MigrateAsync();
        }

        private async UniTask MigrateAsync()
        {
            SetState(RoomSessionState.Migrating);
            string departed = hostMember;
            int knownEpoch = epoch;
            int attemptedEpoch = epoch;
            double deadline = Deadline();
            bool handedOver = false;
            try
            {
                await StopNgoAsync();
                while (State == RoomSessionState.Migrating)
                {
                    if (Time.realtimeSinceStartupAsDouble >= deadline)
                    {
                        Fail();
                        return;
                    }
                    string elected = options.ElectHost(Room.Members, departed);
                    if (elected != null && elected == Room.LocalMemberId)
                    {
                        if (Room.IsOwner)
                        {
                            await StartHostAsync(Math.Max(knownEpoch, ReadEpoch()) + 1);
                            await FinishMigrationAsync();
                            return;
                        }
                    }
                    else if (elected != null && Room.IsOwner && !handedOver)
                    {
                        handedOver = true;
                        await Room.TransferOwnershipAsync(elected);
                    }
                    int published = ReadEpoch();
                    if (published > attemptedEpoch && Room.GetData(HostMemberKey) != departed)
                    {
                        attemptedEpoch = published;
                        if (await ConnectClientAsync(deadline, CancellationToken.None))
                        {
                            await FinishMigrationAsync();
                            return;
                        }
                    }
                    await UniTask.Yield();
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (State == RoomSessionState.Migrating) Fail();
                await StopNgoAsync();
            }
        }

        private async UniTask FinishMigrationAsync()
        {
            if (State != RoomSessionState.Migrating)
            {
                await StopNgoAsync();
                return;
            }
            SetState(RoomSessionState.Connected);
            try { HostMigrated?.Invoke(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private void Fail()
        {
            Unsubscribe();
            SetState(RoomSessionState.Failed);
            StopNgoAsync().Forget();
        }

        private void Close()
        {
            if (State == RoomSessionState.Closed) return;
            Unsubscribe();
            SetState(RoomSessionState.Closed);
        }

        private async UniTask StopNgoAsync()
        {
            if (manager == null) return;
            if (manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
            while (manager != null && manager.ShutdownInProgress) await UniTask.Yield();
        }

        private void SetState(RoomSessionState value)
        {
            if (State == value) return;
            State = value;
            try { StateChanged?.Invoke(value); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private double Deadline() => Time.realtimeSinceStartupAsDouble + options.MigrationTimeout.TotalSeconds;

        private async UniTask<bool> WaitAsync(Func<bool> condition, double deadline, CancellationToken cancellationToken)
        {
            while (!condition())
            {
                if (State == RoomSessionState.Closed || Time.realtimeSinceStartupAsDouble >= deadline) return false;
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            return true;
        }

        private int ReadEpoch() =>
            int.TryParse(Room.GetData(HostEpochKey), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;

        private bool IsMember(string memberId)
        {
            var members = Room.Members;
            for (int i = 0; i < members.Count; i++)
                if (members[i].Id == memberId) return true;
            return false;
        }
    }
}
