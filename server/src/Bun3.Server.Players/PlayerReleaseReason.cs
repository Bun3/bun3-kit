namespace Bun3.Server.Players
{
    /// <summary>Reason for releasing resources without saving player state.</summary>
    public enum PlayerReleaseReason
    {
        /// <summary>The cached player completed normal retirement.</summary>
        Retired,
        /// <summary>A temporary player's operation committed.</summary>
        TemporaryCompleted,
        /// <summary>A temporary player's operation ended without committing.</summary>
        TemporaryAborted,
        /// <summary>The player's memory is no longer authoritative.</summary>
        Invalidated
    }
}
