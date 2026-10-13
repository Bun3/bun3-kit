using System;

namespace Bun3.Server.Players
{
    /// <summary>Identifies the kind of authenticated operation requester.</summary>
    public enum PlayerOperationActorKind
    {
        /// <summary>A player participating in the operation.</summary>
        Player,
        /// <summary>An administrator acting on declared players.</summary>
        Administrator
    }

    /// <summary>Identifies an authenticated requester independently of participating players.</summary>
    public readonly struct PlayerOperationActor : IEquatable<PlayerOperationActor>
    {
        /// <summary>Creates an actor from its kind and stable identifier.</summary>
        public PlayerOperationActor(PlayerOperationActorKind kind, string key)
        { Kind = kind; Key = key; Validate(); }
        /// <summary>Gets the requester kind.</summary>
        public PlayerOperationActorKind Kind { get; }
        /// <summary>Gets the stable requester identifier.</summary>
        public string Key { get; }
        internal void Validate()
        {
            if ((Kind != PlayerOperationActorKind.Player && Kind != PlayerOperationActorKind.Administrator) || string.IsNullOrEmpty(Key))
                throw new ArgumentException("An actor requires a supported kind and a nonempty key.");
        }
        /// <summary>Compares requester kind and identifier using ordinal semantics.</summary>
        public bool Equals(PlayerOperationActor other) => Kind == other.Kind && StringComparer.Ordinal.Equals(Key, other.Key);
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is PlayerOperationActor actor && Equals(actor);
        /// <inheritdoc/>
        public override int GetHashCode() => ((int)Kind * 397) ^ (Key == null ? 0 : StringComparer.Ordinal.GetHashCode(Key));
    }
}
