using System;
using System.Collections.Generic;

namespace Bun3.Server.Players
{
    /// <summary>Provides participant access only during synchronous validation and application.</summary>
    public sealed class PlayerOperationContext<TPlayer> where TPlayer : Player
    {
        private readonly Guid _operationId;
        private readonly IReadOnlyList<string> _keys;
        private TPlayer[]? _players;
        internal PlayerOperationContext(Guid operationId, IReadOnlyList<string> keys, TPlayer[] players)
        { _operationId = operationId; _keys = keys; _players = players; }
        /// <summary>Gets the current operation ID.</summary>
        public Guid OperationId { get { EnsureActive(); return _operationId; } }
        /// <summary>Gets the declared participant keys.</summary>
        public IReadOnlyList<string> ParticipantKeys { get { EnsureActive(); return _keys; } }
        /// <summary>Returns a declared participant; access after the callback lifetime is rejected.</summary>
        public TPlayer GetPlayer(string key)
        {
            EnsureActive();
            for (var i = 0; i < _keys.Count; i++) if (StringComparer.Ordinal.Equals(_keys[i], key)) return _players![i];
            throw new ArgumentException("Player is outside the operation.", nameof(key));
        }
        internal void Close() => _players = null;
        private void EnsureActive()
        { if (_players == null) throw new ObjectDisposedException(nameof(PlayerOperationContext<TPlayer>)); }
    }
}
