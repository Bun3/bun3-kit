using System;
using System.Collections.Generic;

namespace Bun3.Server.Players
{
    /// <summary>An immutable operation identity and its committed serialized result.</summary>
    public sealed class PlayerOperationRecord
    {
        /// <summary>Copies a receipt requested by a player or administrator.</summary>
        public PlayerOperationRecord(Guid id, string kind, PlayerOperationActor actor, IReadOnlyList<string> participantKeys,
            ReadOnlyMemory<byte> inputFingerprint, int resultSchemaVersion, ReadOnlyMemory<byte> resultPayload)
        {
            if (id == Guid.Empty) throw new ArgumentException("Operation ID is empty.", nameof(id));
            if (string.IsNullOrEmpty(kind)) throw new ArgumentException("Operation kind is empty.", nameof(kind));
            actor.Validate();
            if (participantKeys == null) throw new ArgumentNullException(nameof(participantKeys));
            if (inputFingerprint.Length != 32) throw new ArgumentException("Fingerprint must contain 32 bytes.", nameof(inputFingerprint));
            var keys = new string[participantKeys.Count];
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i] = participantKeys[i];
                if (string.IsNullOrEmpty(keys[i]) || (i > 0 && StringComparer.Ordinal.Compare(keys[i - 1], keys[i]) >= 0))
                    throw new ArgumentException("Participant keys must be nonempty, sorted, and unique.", nameof(participantKeys));
            }
            if (keys.Length == 0 || (actor.Kind == PlayerOperationActorKind.Player && Array.BinarySearch(keys, actor.Key, StringComparer.Ordinal) < 0))
                throw new ArgumentException("Player actors must belong to a nonempty participant set.", nameof(participantKeys));
            Id = id; Kind = kind; Actor = actor; ParticipantKeys = Array.AsReadOnly(keys);
            InputFingerprint = inputFingerprint.ToArray(); ResultSchemaVersion = resultSchemaVersion; ResultPayload = resultPayload.ToArray();
        }
        /// <summary>Copies a receipt requested by a participating player.</summary>
        public PlayerOperationRecord(Guid id, string kind, string actorKey, IReadOnlyList<string> participantKeys,
            ReadOnlyMemory<byte> inputFingerprint, int resultSchemaVersion, ReadOnlyMemory<byte> resultPayload)
            : this(id, kind, new PlayerOperationActor(PlayerOperationActorKind.Player, actorKey), participantKeys,
                inputFingerprint, resultSchemaVersion, resultPayload) { }
        /// <summary>Gets the authenticated requester.</summary>
        public PlayerOperationActor Actor { get; }
        /// <summary>Gets the operation ID.</summary>
        public Guid Id { get; }
        /// <summary>Gets the operation kind.</summary>
        public string Kind { get; }
        /// <summary>Gets the authenticated actor key.</summary>
        public string ActorKey => Actor.Key;
        /// <summary>Gets the sorted participant keys.</summary>
        public IReadOnlyList<string> ParticipantKeys { get; }
        /// <summary>Gets the copied 32-byte input fingerprint.</summary>
        public ReadOnlyMemory<byte> InputFingerprint { get; }
        /// <summary>Gets the result serialization schema version.</summary>
        public int ResultSchemaVersion { get; }
        /// <summary>Gets the owned serialized result.</summary>
        public ReadOnlyMemory<byte> ResultPayload { get; }
    }
}
