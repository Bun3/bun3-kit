namespace Bun3.Server.Players
{
    /// <summary>Classifies the durable outcome of a player operation.</summary>
    public enum PlayerOperationOutcome
    {
        /// <summary>The operation committed in this execution.</summary>
        Committed,
        /// <summary>A matching committed receipt supplied the result.</summary>
        AlreadyCommitted,
        /// <summary>The operation was rejected before mutation.</summary>
        Rejected,
        /// <summary>The operation failed without a confirmed commit.</summary>
        Failed,
        /// <summary>The database outcome could not be confirmed.</summary>
        Unknown
    }

    /// <summary>An operation outcome with a frozen successful result or a nonzero failure status.</summary>
    public sealed class PlayerOperationResult<TResult> where TResult : class
    {
        internal PlayerOperationResult(PlayerOperationOutcome outcome, int status, TResult? value = null)
        { Outcome = outcome; Status = status; Value = value; }
        /// <summary>Gets the durable outcome classification.</summary>
        public PlayerOperationOutcome Outcome { get; }
        /// <summary>Gets zero on success or the framework/domain failure code.</summary>
        public int Status { get; }
        /// <summary>Gets the reconstructed successful result; null for every failure.</summary>
        public TResult? Value { get; }
    }
}
