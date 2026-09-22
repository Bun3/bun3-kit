using UnityEngine;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Immutable, domain-independent event payload. Numeric validation occurs on submission.</summary>
    public readonly struct SoundEventData
    {
        /// <summary>Creates an event payload.</summary>
        public SoundEventData(ulong sessionId, ulong eventId, ulong sourceId, int kindId,
            Vector3 position, float intensity, float radius, ulong hostTick, uint revision)
        {
            SessionId = sessionId; EventId = eventId; SourceId = sourceId; KindId = kindId;
            Position = position; Intensity = intensity; Radius = radius; HostTick = hostTick; Revision = revision;
        }
        /// <summary>Gets the caller-owned session identifier.</summary>
        public ulong SessionId { get; }
        /// <summary>Gets the caller-owned event identifier.</summary>
        public ulong EventId { get; }
        /// <summary>Gets the source identifier used for bulk cleanup.</summary>
        public ulong SourceId { get; }
        /// <summary>Gets the caller-defined event kind.</summary>
        public int KindId { get; }
        /// <summary>Gets the source position.</summary>
        public Vector3 Position { get; }
        /// <summary>Gets intensity independently of propagation radius.</summary>
        public float Intensity { get; }
        /// <summary>Gets the propagation radius.</summary>
        public float Radius { get; }
        /// <summary>Gets the caller-owned creation tick.</summary>
        public ulong HostTick { get; }
        /// <summary>Gets the caller-owned revision.</summary>
        public uint Revision { get; }
    }
}
