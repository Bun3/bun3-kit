using System;

namespace Bun3.Server.Players
{
    /// <summary>Confirms that an operator has stopped the old process and prevented automatic restart.</summary>
    public sealed class PlayerWriteRecoveryConfirmation
    {
        /// <summary>Creates a recovery request after independently verifying process termination and restart prevention.</summary>
        public PlayerWriteRecoveryConfirmation(Guid id, Guid expectedInstanceId, string operatorKey, string reason)
        {
            if (id == Guid.Empty) throw new ArgumentException("A confirmation ID is required.", nameof(id));
            if (expectedInstanceId == Guid.Empty) throw new ArgumentException("The old instance ID is required.", nameof(expectedInstanceId));
            if (string.IsNullOrWhiteSpace(operatorKey)) throw new ArgumentException("An operator identity is required.", nameof(operatorKey));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A recovery reason is required.", nameof(reason));
            Id = id; ExpectedInstanceId = expectedInstanceId; OperatorKey = operatorKey; Reason = reason;
        }
        /// <summary>Gets the stable confirmation ID used on retries.</summary>
        public Guid Id { get; }
        /// <summary>Gets the instance whose process has been stopped.</summary>
        public Guid ExpectedInstanceId { get; }
        /// <summary>Gets the authenticated operator who performed the confirmation.</summary>
        public string OperatorKey { get; }
        /// <summary>Gets the reason and confirmation context for the audit.</summary>
        public string Reason { get; }
    }
}
