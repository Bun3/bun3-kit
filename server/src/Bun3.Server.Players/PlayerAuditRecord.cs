using System;

namespace Bun3.Server.Players
{
    /// <summary>An immutable audit event persisted with its player's state.</summary>
    public sealed class PlayerAuditRecord
    {
        /// <summary>Copies an event and its serialized payload.</summary>
        public PlayerAuditRecord(Guid operationId, string eventName, DateTime occurredAtUtc, ReadOnlyMemory<byte> payload)
        {
            if (string.IsNullOrEmpty(eventName)) throw new ArgumentException("Event name is empty.", nameof(eventName));
            if (occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Event time must be UTC.", nameof(occurredAtUtc));
            OperationId = operationId; EventName = eventName; OccurredAtUtc = occurredAtUtc; Payload = payload.ToArray();
        }
        /// <summary>Gets the operation that produced the event.</summary>
        public Guid OperationId { get; }
        /// <summary>Gets the domain event name.</summary>
        public string EventName { get; }
        /// <summary>Gets the UTC event time.</summary>
        public DateTime OccurredAtUtc { get; }
        /// <summary>Gets the owned serialized event payload.</summary>
        public ReadOnlyMemory<byte> Payload { get; }
    }
}
