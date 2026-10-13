using System;
using System.Collections.Generic;

namespace Bun3.Server.Players
{
    /// <summary>The immutable, authenticated input accepted for one administrator command.</summary>
    public sealed class PlayerCommandEnvelope
    {
        /// <summary>Copies a command's identity, participants, fingerprint and serialized input.</summary>
        public PlayerCommandEnvelope(Guid id, string scopeKey, PlayerOperationActor actor, string kind, int inputSchemaVersion,
            IReadOnlyList<string> participantKeys, ReadOnlyMemory<byte> inputFingerprint, ReadOnlyMemory<byte> inputPayload)
        {
            if (id == Guid.Empty) throw new ArgumentException("A command ID is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(scopeKey)) throw new ArgumentException("A scope key is required.", nameof(scopeKey));
            actor.Validate();
            if (actor.Kind != PlayerOperationActorKind.Administrator) throw new ArgumentException("Commands require an administrator actor.", nameof(actor));
            if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("A command kind is required.", nameof(kind));
            if (inputSchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(inputSchemaVersion));
            if (participantKeys == null) throw new ArgumentNullException(nameof(participantKeys));
            if (inputFingerprint.Length != 32) throw new ArgumentException("A SHA-256 fingerprint is required.", nameof(inputFingerprint));
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < participantKeys.Count; i++)
            {
                if (string.IsNullOrEmpty(participantKeys[i])) throw new ArgumentException("Participant keys cannot be empty.", nameof(participantKeys));
                keys.Add(participantKeys[i]);
            }
            if (keys.Count == 0) throw new ArgumentException("At least one participant is required.", nameof(participantKeys));
            var copy = new string[keys.Count]; keys.CopyTo(copy);
            Id = id; ScopeKey = scopeKey; Actor = actor; Kind = kind; InputSchemaVersion = inputSchemaVersion;
            ParticipantKeys = Array.AsReadOnly(copy); InputFingerprint = inputFingerprint.ToArray(); InputPayload = inputPayload.ToArray();
        }
        /// <summary>Gets the command ID, also used as the operation ID.</summary>
        public Guid Id { get; }
        /// <summary>Gets the ownership scope.</summary>
        public string ScopeKey { get; }
        /// <summary>Gets the authenticated administrator identity.</summary>
        public PlayerOperationActor Actor { get; }
        /// <summary>Gets the registered command kind.</summary>
        public string Kind { get; }
        /// <summary>Gets the input schema version.</summary>
        public int InputSchemaVersion { get; }
        /// <summary>Gets sorted, unique, copied participant keys.</summary>
        public IReadOnlyList<string> ParticipantKeys { get; }
        /// <summary>Gets the copied input identity fingerprint.</summary>
        public ReadOnlyMemory<byte> InputFingerprint { get; }
        /// <summary>Gets the copied serialized command input.</summary>
        public ReadOnlyMemory<byte> InputPayload { get; }
        internal bool Matches(PlayerCommandEnvelope other)
        {
            if (Id != other.Id || ScopeKey != other.ScopeKey || !Actor.Equals(other.Actor) || Kind != other.Kind ||
                InputSchemaVersion != other.InputSchemaVersion || !InputFingerprint.Span.SequenceEqual(other.InputFingerprint.Span) ||
                !InputPayload.Span.SequenceEqual(other.InputPayload.Span) || ParticipantKeys.Count != other.ParticipantKeys.Count) return false;
            for (var i = 0; i < ParticipantKeys.Count; i++) if (ParticipantKeys[i] != other.ParticipantKeys[i]) return false;
            return true;
        }
        internal bool Matches<TPlayer, TResult>(PlayerOperation<TPlayer, TResult> operation) where TPlayer : Player where TResult : class
        {
            if (Id != operation.Id || !Actor.Equals(operation.Actor) || Kind != operation.Kind ||
                !InputFingerprint.Span.SequenceEqual(operation.InputFingerprint.Span) || ParticipantKeys.Count != operation.ParticipantKeys.Count) return false;
            for (var i = 0; i < ParticipantKeys.Count; i++) if (ParticipantKeys[i] != operation.ParticipantKeys[i]) return false;
            return true;
        }
    }
}
