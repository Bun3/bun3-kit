using System;

namespace Bun3.Server.Players
{
    /// <summary>Signals that a persistence transaction has no write authority.</summary>
    public sealed class PlayerWriteAuthorityException : InvalidOperationException
    {
        /// <summary>Creates a denial for a missing or incompatible control row.</summary>
        public PlayerWriteAuthorityException(string scopeKey, PlayerWriteAuthorityRecord? record)
            : base("Player write authority is unavailable for this writer.") { ScopeKey = scopeKey; Record = record; }
        /// <summary>Gets the affected ownership scope.</summary>
        public string ScopeKey { get; }
        /// <summary>Gets the observed control row, or null when absent.</summary>
        public PlayerWriteAuthorityRecord? Record { get; }
    }
}
