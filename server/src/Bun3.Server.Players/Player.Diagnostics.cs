// Owns pending audit bytes until their state transaction commits.
using System;
using System.Collections.Generic;
namespace Bun3.Server.Players
{
    public abstract partial class Player
    {
        private List<PlayerAuditRecord>? _audit;
        /// <summary>Copies a serialized domain event into the current mutation's pending audit batch.</summary>
        /// <remarks>Requires a store-backed registry and an owned request, posted work, lifecycle mutation, or operation Apply.
        /// Loader-only registries cannot persist this batch and reject the call before buffering or marking dirty.</remarks>
        public void RecordAudit(string eventName, ReadOnlySpan<byte> payload)
        {
            var lane = PlayerExecutionLane.Current;
            if (!IsValid || lane == null || !lane.CanMutate(this))
                throw new InvalidOperationException("Audit requires the player's active mutation scope.");
            if (!lane.SupportsAudit)
                throw new InvalidOperationException("Audit requires a store-backed player registry.");
            var record = new PlayerAuditRecord(lane.MutationId, eventName, DateTime.UtcNow, payload.ToArray());
            (_audit ??= new List<PlayerAuditRecord>()).Add(record);
            MarkDirty();
        }
        internal IReadOnlyList<PlayerAuditRecord> CaptureAudit() => _audit == null || _audit.Count == 0
            ? Array.Empty<PlayerAuditRecord>() : _audit.ToArray();
        internal void CompleteAudit(int count) { if (count != 0) _audit!.RemoveRange(0, count); }
        internal void DiscardAudit() => _audit?.Clear();
    }
}
