using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Rooms
{
    /// <summary>A member of a room.</summary>
    public readonly struct RoomMember
    {
        /// <summary>Creates a member record.</summary>
        public RoomMember(string id, long joinedAt)
        {
            Id = id;
            JoinedAt = joinedAt;
        }

        /// <summary>Backend-stable member identifier. A Steam backend uses the decimal SteamID.</summary>
        public string Id { get; }

        /// <summary>Join time in backend units; earlier joins have smaller values.</summary>
        public long JoinedAt { get; }
    }

    /// <summary>
    /// A joined room. Members, ownership and data come from the backend. Events are raised on the main thread.
    /// </summary>
    public interface IRoom
    {
        /// <summary>Backend room identifier, accepted by <see cref="IRoomBackend.JoinByIdAsync"/>.</summary>
        string Id { get; }

        /// <summary>Code players type to join.</summary>
        RoomCode Code { get; }

        /// <summary>Display name given at creation; null when the backend does not know it.</summary>
        string Name { get; }

        /// <summary>Current members, owner included.</summary>
        IReadOnlyList<RoomMember> Members { get; }

        /// <summary>Member capacity including the owner; 0 when the backend does not know it.</summary>
        int MaxMembers { get; }

        /// <summary>Id of the local member; null until the backend knows it.</summary>
        string LocalMemberId { get; }

        /// <summary>Id of the member allowed to change the room.</summary>
        string OwnerId { get; }

        /// <summary>Whether the local member owns the room.</summary>
        bool IsOwner { get; }

        /// <summary>Whether the room accepts new members: the owner's latest <see cref="SetJoinableAsync"/>, true when created.</summary>
        bool IsJoinable { get; }

        /// <summary>Whether the room appears in <see cref="IRoomBackend.ListPublicAsync"/>; false when created.</summary>
        bool IsPublic { get; }

        /// <summary>Room data for <paramref name="key"/>; null when absent.</summary>
        string GetData(string key);

        /// <summary>Stores room data. Owner only.</summary>
        UniTask SetDataAsync(string key, string value);

        /// <summary>Opens or closes the room to new members. Closed rooms are not found by code. Owner only.</summary>
        UniTask SetJoinableAsync(bool joinable);

        /// <summary>Shows or hides the room in public lists. Code and invite joins work either way. Owner only.</summary>
        UniTask SetPublicAsync(bool isPublic);

        /// <summary>Makes <paramref name="memberId"/> the owner. Owner only.</summary>
        UniTask TransferOwnershipAsync(string memberId);

        /// <summary>Leaves the room. Idempotent; does not raise <see cref="Closed"/>.</summary>
        UniTask LeaveAsync();

        /// <summary>Raised when members join, leave or change their join records.</summary>
        event Action MembersChanged;

        /// <summary>Raised when <see cref="OwnerId"/> changes.</summary>
        event Action OwnerChanged;

        /// <summary>
        /// Raised when room data or <see cref="IsJoinable"/> may have changed. Read the keys you need again;
        /// backends do not report which key changed.
        /// </summary>
        event Action DataChanged;

        /// <summary>Raised when the room ends without <see cref="LeaveAsync"/>: kicked, destroyed or host gone.</summary>
        event Action Closed;
    }
}
