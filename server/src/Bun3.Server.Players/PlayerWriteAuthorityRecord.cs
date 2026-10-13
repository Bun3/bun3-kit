using System;

namespace Bun3.Server.Players
{
    /// <summary>Defines the durable write ownership state of a player scope.</summary>
    public enum PlayerWriteState
    {
        /// <summary>Fresh-state administrator operations are permitted.</summary>
        Offline,
        /// <summary>The claimed server is initializing.</summary>
        Starting,
        /// <summary>The claimed server owns all player changes.</summary>
        Online,
        /// <summary>The claimed server is completing admitted work and final saves.</summary>
        Draining,
        /// <summary>Operator-confirmed recovery is required.</summary>
        RecoveryRequired
    }

    /// <summary>An immutable snapshot of the durable control row.</summary>
    public sealed class PlayerWriteAuthorityRecord
    {
        /// <summary>Creates a record read or written by the database mapping.</summary>
        public PlayerWriteAuthorityRecord(PlayerWriteState state, Guid? instanceId, Guid transitionId, ReadOnlyMemory<byte> transitionFingerprint)
        {
            ValidateOwner(state, instanceId);
            if (transitionId == Guid.Empty) throw new ArgumentException("A transition ID is required.", nameof(transitionId));
            if (transitionFingerprint.Length != 32) throw new ArgumentException("A SHA-256 fingerprint is required.", nameof(transitionFingerprint));
            State = state; InstanceId = instanceId; TransitionId = transitionId; TransitionFingerprint = transitionFingerprint.ToArray();
        }
        /// <summary>Gets the durable state.</summary>
        public PlayerWriteState State { get; }
        /// <summary>Gets the current or recovering server instance, if any.</summary>
        public Guid? InstanceId { get; }
        /// <summary>Gets the most recent committed transition ID.</summary>
        public Guid TransitionId { get; }
        /// <summary>Gets the copied transition identity fingerprint.</summary>
        public ReadOnlyMemory<byte> TransitionFingerprint { get; }
        internal static void ValidateOwner(PlayerWriteState state, Guid? owner)
        {
            if (state < PlayerWriteState.Offline || state > PlayerWriteState.RecoveryRequired || owner == Guid.Empty ||
                (state == PlayerWriteState.Offline ? owner.HasValue : !owner.HasValue))
                throw new ArgumentException("Invalid write authority state or instance ID.");
        }
    }
}
