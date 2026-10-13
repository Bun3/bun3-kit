using System;

namespace Bun3.Server.Players
{
    /// <summary>Describes command acceptance or execution progress.</summary>
    public enum PlayerCommandStatus
    {
        /// <summary>Accepted and awaiting execution.</summary>
        Pending,
        /// <summary>Player changes and the stored result committed atomically.</summary>
        Committed,
        /// <summary>Domain validation rejected the command without player changes.</summary>
        Rejected,
        /// <summary>Execution rolled back and failure was durably recorded.</summary>
        Failed,
        /// <summary>A response-only state whose database outcome needs confirmation.</summary>
        Unknown
    }

    /// <summary>A command input and its copied durable result or uncertain response.</summary>
    public sealed class PlayerCommandRecord
    {
        /// <summary>Creates a command record, copying result bytes.</summary>
        public PlayerCommandRecord(PlayerCommandEnvelope envelope, DateTimeOffset acceptedAtUtc, PlayerCommandStatus status,
            int statusCode, int resultSchemaVersion, ReadOnlyMemory<byte> resultPayload)
        {
            Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
            if (status < PlayerCommandStatus.Pending || status > PlayerCommandStatus.Unknown) throw new ArgumentOutOfRangeException(nameof(status));
            if (resultSchemaVersion < 0 || (status == PlayerCommandStatus.Committed && resultSchemaVersion == 0)) throw new ArgumentOutOfRangeException(nameof(resultSchemaVersion));
            AcceptedAtUtc = acceptedAtUtc.ToUniversalTime(); Status = status; StatusCode = statusCode;
            ResultSchemaVersion = resultSchemaVersion; ResultPayload = resultPayload.ToArray();
        }
        /// <summary>Gets the accepted immutable input.</summary>
        public PlayerCommandEnvelope Envelope { get; }
        /// <summary>Gets the database acceptance time in UTC.</summary>
        public DateTimeOffset AcceptedAtUtc { get; }
        /// <summary>Gets the durable status, or Unknown for an unconfirmed response.</summary>
        public PlayerCommandStatus Status { get; }
        /// <summary>Gets the result or rejection status code.</summary>
        public int StatusCode { get; }
        /// <summary>Gets the serialized result schema, or zero when absent.</summary>
        public int ResultSchemaVersion { get; }
        /// <summary>Gets the copied serialized result.</summary>
        public ReadOnlyMemory<byte> ResultPayload { get; }
        internal PlayerCommandRecord With(PlayerCommandStatus status, int code, int schema = 0, ReadOnlyMemory<byte> payload = default)
            => new PlayerCommandRecord(Envelope, AcceptedAtUtc, status, code, schema, payload);
        internal bool IsTerminal => Status == PlayerCommandStatus.Committed || Status == PlayerCommandStatus.Rejected || Status == PlayerCommandStatus.Failed;
    }

    /// <summary>Signals reuse of a command ID with different immutable input.</summary>
    public sealed class PlayerCommandConflictException : InvalidOperationException
    {
        /// <summary>Creates a conflict for the supplied command ID.</summary>
        public PlayerCommandConflictException(Guid commandId) : base("Command identity differs from its accepted input.") => CommandId = commandId;
        /// <summary>Gets the conflicting command ID.</summary>
        public Guid CommandId { get; }
    }
}
