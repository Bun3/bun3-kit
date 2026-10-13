using System;

namespace Bun3.Server.Players
{
    /// <summary>Identifies the kind of writer using a persistence scope.</summary>
    public enum PlayerWriteRole
    {
        /// <summary>A server instance that owns the player cache.</summary>
        Server,
        /// <summary>An administrator using fresh state while the server is offline.</summary>
        OfflineAdministrator
    }

    /// <summary>Binds a store to its immutable writer identity.</summary>
    public readonly struct PlayerWriteBinding
    {
        /// <summary>Creates a binding. Servers require a nonempty instance ID; administrators require an empty one.</summary>
        public PlayerWriteBinding(PlayerWriteRole role, Guid instanceId)
        { Role = role; InstanceId = instanceId; Validate(); }
        /// <summary>Gets the writer role.</summary>
        public PlayerWriteRole Role { get; }
        /// <summary>Gets the unique ID of this server process lifetime.</summary>
        public Guid InstanceId { get; }
        internal void Validate()
        {
            if ((Role != PlayerWriteRole.Server && Role != PlayerWriteRole.OfflineAdministrator) ||
                (Role == PlayerWriteRole.Server ? InstanceId == Guid.Empty : InstanceId != Guid.Empty))
                throw new ArgumentException("Invalid player writer binding.");
        }
    }

    internal enum PlayerStoreAccess { Load, Save, Operation, Recovery }
}
