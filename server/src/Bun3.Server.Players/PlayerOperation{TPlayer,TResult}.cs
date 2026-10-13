using System;
using System.Collections.Generic;

namespace Bun3.Server.Players
{
    /// <summary>A synchronous domain operation over a fixed set of exclusively owned players.</summary>
    public abstract class PlayerOperation<TPlayer, TResult> where TPlayer : Player where TResult : class
    {
        /// <summary>Creates an operation requested by a player or administrator.</summary>
        protected PlayerOperation(Guid id, string kind, PlayerOperationActor actor, IReadOnlyList<string> participantKeys,
            ReadOnlyMemory<byte> inputFingerprint, IPlayerResultCodec<TResult> codec)
        {
            if (id == Guid.Empty) throw new ArgumentException("Operation ID is empty.", nameof(id));
            if (string.IsNullOrEmpty(kind)) throw new ArgumentException("Operation kind is empty.", nameof(kind));
            actor.Validate();
            if (participantKeys == null) throw new ArgumentNullException(nameof(participantKeys));
            if (inputFingerprint.Length != 32) throw new ArgumentException("Fingerprint must contain 32 bytes.", nameof(inputFingerprint));
            Codec = codec ?? throw new ArgumentNullException(nameof(codec));
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < participantKeys.Count; i++)
            {
                if (string.IsNullOrEmpty(participantKeys[i])) throw new ArgumentException("Player key is empty.", nameof(participantKeys));
                keys.Add(participantKeys[i]);
            }
            if (keys.Count == 0 || (actor.Kind == PlayerOperationActorKind.Player && !keys.Contains(actor.Key)))
                throw new ArgumentException("Player actors must belong to a nonempty participant set.", nameof(participantKeys));
            var copy = new string[keys.Count]; keys.CopyTo(copy);
            Id = id; Kind = kind; Actor = actor; ParticipantKeys = Array.AsReadOnly(copy);
            InputFingerprint = inputFingerprint.ToArray();
        }
        /// <summary>Copies the identity of a player-requested operation.</summary>
        protected PlayerOperation(Guid id, string kind, string actorKey, IReadOnlyList<string> participantKeys,
            ReadOnlyMemory<byte> inputFingerprint, IPlayerResultCodec<TResult> codec)
            : this(id, kind, new PlayerOperationActor(PlayerOperationActorKind.Player, actorKey), participantKeys, inputFingerprint, codec) { }
        /// <summary>Gets the authenticated requester.</summary>
        public PlayerOperationActor Actor { get; }
        /// <summary>Gets the stable operation ID used for retries.</summary>
        public Guid Id { get; }
        /// <summary>Gets the domain operation kind.</summary>
        public string Kind { get; }
        /// <summary>Gets the authenticated actor key.</summary>
        public string ActorKey => Actor.Key;
        /// <summary>Gets the owned ordinal sorted unique participant keys.</summary>
        public IReadOnlyList<string> ParticipantKeys { get; }
        /// <summary>Gets the copied 32-byte canonical input fingerprint.</summary>
        public ReadOnlyMemory<byte> InputFingerprint { get; }
        /// <summary>Gets the result codec.</summary>
        public IPlayerResultCodec<TResult> Codec { get; }
        /// <summary>Reads state without mutation and returns zero or a negative domain rejection code.</summary>
        public abstract int Validate(PlayerOperationContext<TPlayer> context);
        /// <summary>Completes all synchronous mutations and domain callbacks and returns a non-null result.</summary>
        public abstract TResult Apply(PlayerOperationContext<TPlayer> context);
    }
}
