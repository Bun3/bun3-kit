using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Rooms
{
    /// <summary>Options for <see cref="IRoomBackend.CreateAsync"/>.</summary>
    public struct RoomCreateOptions
    {
        /// <summary>Member capacity including the owner.</summary>
        public int MaxMembers;

        /// <summary>Display name stored with the room.</summary>
        public string Name;

        /// <summary>Compatibility string. Joiners must pass an identical value.</summary>
        public string Version;
    }

    /// <summary>Options for joining a room.</summary>
    public struct RoomJoinOptions
    {
        /// <summary>Compared ordinally with the room's <see cref="RoomCreateOptions.Version"/>.</summary>
        public string Version;
    }

    /// <summary>
    /// Creates and joins rooms on one platform. One instance holds at most one room at a time;
    /// creating or joining while in a room throws <see cref="InvalidOperationException"/>.
    /// Failures the player should see are <see cref="RoomException"/>.
    /// </summary>
    public interface IRoomBackend
    {
        /// <summary>Creates a room owned by the local member.</summary>
        UniTask<IRoom> CreateAsync(RoomCreateOptions options, CancellationToken cancellationToken = default);

        /// <summary>Joins the joinable room with <paramref name="code"/>.</summary>
        UniTask<IRoom> JoinByCodeAsync(RoomCode code, RoomJoinOptions options, CancellationToken cancellationToken = default);

        /// <summary>Joins a room by backend id, as delivered by <see cref="JoinRequested"/>.</summary>
        UniTask<IRoom> JoinByIdAsync(string roomId, RoomJoinOptions options, CancellationToken cancellationToken = default);

        /// <summary>Raised with a room id when the player accepts an invite or joins a friend from the platform.</summary>
        event Action<string> JoinRequested;

        /// <summary>Opens the platform invite UI for <paramref name="room"/>; false when unsupported.</summary>
        bool TryOpenInviteOverlay(IRoom room);
    }
}
