using System;

namespace Bun3.Unity.Rooms
{
    /// <summary>Why a room operation failed. Player-facing text is the game's choice.</summary>
    public enum RoomFailure
    {
        /// <summary>No joinable room matches. Closed (locked) rooms report this too.</summary>
        NotFound,

        /// <summary>The room has no free member slot.</summary>
        Full,

        /// <summary>The room's version string differs from the joiner's.</summary>
        VersionMismatch,

        /// <summary>The platform service is not running, not signed in, or did not answer in time.</summary>
        ServiceUnavailable,
    }

    /// <summary>A room operation failure the player should be told about.</summary>
    public sealed class RoomException : Exception
    {
        /// <summary>Creates the exception; the message is the failure name.</summary>
        public RoomException(RoomFailure failure) : base(failure.ToString()) => Failure = failure;

        /// <summary>What went wrong.</summary>
        public RoomFailure Failure { get; }
    }
}
