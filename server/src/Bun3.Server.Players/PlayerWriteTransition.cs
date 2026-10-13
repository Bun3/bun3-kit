using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Bun3.Server.Players
{
    /// <summary>Describes a conditional ownership transition and its audit identity.</summary>
    public sealed class PlayerWriteTransition
    {
        /// <summary>Creates an immutable transition request.</summary>
        public PlayerWriteTransition(Guid id, PlayerWriteState expectedState, Guid? expectedInstanceId,
            PlayerWriteState nextState, Guid? nextInstanceId, string operatorKey, string reason)
        {
            if (id == Guid.Empty) throw new ArgumentException("A transition ID is required.", nameof(id));
            PlayerWriteAuthorityRecord.ValidateOwner(expectedState, expectedInstanceId);
            PlayerWriteAuthorityRecord.ValidateOwner(nextState, nextInstanceId);
            if (string.IsNullOrWhiteSpace(operatorKey)) throw new ArgumentException("An operator identity is required.", nameof(operatorKey));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A transition reason is required.", nameof(reason));
            Id = id; ExpectedState = expectedState; ExpectedInstanceId = expectedInstanceId;
            NextState = nextState; NextInstanceId = nextInstanceId; OperatorKey = operatorKey; Reason = reason;
        }
        /// <summary>Gets the idempotency ID.</summary>
        public Guid Id { get; }
        /// <summary>Gets the required current state.</summary>
        public PlayerWriteState ExpectedState { get; }
        /// <summary>Gets the required current instance.</summary>
        public Guid? ExpectedInstanceId { get; }
        /// <summary>Gets the requested state.</summary>
        public PlayerWriteState NextState { get; }
        /// <summary>Gets the requested instance.</summary>
        public Guid? NextInstanceId { get; }
        /// <summary>Gets the authenticated operator or server identity.</summary>
        public string OperatorKey { get; }
        /// <summary>Gets the audit reason.</summary>
        public string Reason { get; }
        internal byte[] Fingerprint(string scopeKey)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(scopeKey); writer.Write(Id.ToByteArray()); writer.Write((int)ExpectedState);
                writer.Write(ExpectedInstanceId.HasValue); if (ExpectedInstanceId.HasValue) writer.Write(ExpectedInstanceId.Value.ToByteArray());
                writer.Write((int)NextState); writer.Write(NextInstanceId.HasValue);
                if (NextInstanceId.HasValue) writer.Write(NextInstanceId.Value.ToByteArray());
                writer.Write(OperatorKey); writer.Write(Reason);
            }
            using var sha = SHA256.Create();
            return sha.ComputeHash(stream.ToArray());
        }
    }

    /// <summary>Classifies an ownership transition result.</summary>
    public enum PlayerWriteTransitionOutcome
    {
        /// <summary>The transition is durably confirmed.</summary>
        Committed,
        /// <summary>The current ownership or transition identity differs.</summary>
        Conflict,
        /// <summary>A commit was attempted but its outcome is not confirmed.</summary>
        Unknown
    }

    /// <summary>Contains the outcome and the control row observed by a transition.</summary>
    public sealed class PlayerWriteTransitionResult
    {
        internal PlayerWriteTransitionResult(PlayerWriteTransitionOutcome outcome, PlayerWriteAuthorityRecord record, Exception? error = null)
        { Outcome = outcome; Record = record; Error = error; }
        /// <summary>Gets the confirmed or uncertain outcome.</summary>
        public PlayerWriteTransitionOutcome Outcome { get; }
        /// <summary>Gets the observed row, or the proposed row when the commit outcome is unknown.</summary>
        public PlayerWriteAuthorityRecord Record { get; }
        /// <summary>Gets the commit error, if any.</summary>
        public Exception? Error { get; }
    }
}
