using System;
using System.Threading;
using System.Collections.Generic;

namespace Bun3.Server.Players
{
    internal sealed class PlayerExecutionLane : IDisposable
    {
        private static readonly AsyncLocal<PlayerExecutionLane?> Ambient = new AsyncLocal<PlayerExecutionLane?>();
        private readonly PlayerExecutionLane? _previous;
        private PlayerExecutionScope _scope;
        internal PlayerOperationNotifications? Notifications;
        private Guid _mutationId;
        private Player? _mutationPlayer;
        private IReadOnlyList<Player>? _mutationPlayers;
        internal bool SupportsAudit { get; private set; }
        internal Guid MutationId => _mutationId == Guid.Empty ? (_mutationId = Guid.NewGuid()) : _mutationId;
        internal bool CanMutate(Player player)
        {
            if (!OwnsExecution) return false;
            if (ReferenceEquals(_mutationPlayer, player)) return true;
            if (_mutationPlayers != null)
                for (var i = 0; i < _mutationPlayers.Count; i++)
                    if (ReferenceEquals(_mutationPlayers[i], player)) return true;
            return false;
        }
        internal void SetMutation(Player player, bool supportsAudit)
        { _mutationPlayer = player; SupportsAudit = supportsAudit; }
        internal void SetMutation(IReadOnlyList<Player> players, Guid id, bool supportsAudit)
        { _mutationPlayers = players; _mutationId = id; SupportsAudit = supportsAudit; }
        internal void EndMutation() { _mutationPlayer = null; _mutationPlayers = null; SupportsAudit = false; }

        internal static PlayerExecutionLane? Current => Ambient.Value;
        internal object? Session { get; }
        internal bool InRequest { get; private set; }
        internal bool OwnsExecution { get; private set; }

        private PlayerExecutionLane(object? session)
        {
            _previous = Ambient.Value;
            Session = session;
            Ambient.Value = this;
        }

        // Must be called synchronously from the outer execution boundary.
        internal static PlayerExecutionLane Install(object? session = null) => new PlayerExecutionLane(session);

        internal static void ThrowIfOwned()
        {
            var lane = Current;
            if (lane != null && lane.OwnsExecution)
                throw new InvalidOperationException("Nested player execution or lifecycle entry is not supported.");
        }

        internal void BeginRequest()
        {
            if (InRequest || OwnsExecution) throw new InvalidOperationException("Player execution is already active.");
            InRequest = true;
        }

        internal void TakeOwnership(PlayerExecutionScope scope)
        {
            if (OwnsExecution) throw new InvalidOperationException("Player execution is already owned.");
            _scope = scope;
            OwnsExecution = true;
        }

        internal void EndRequest()
        {
            ReleaseOwnership();
            InRequest = false;
        }

        internal void ReleaseOwnership()
        {
            _scope.Dispose();
            _scope = default;
            OwnsExecution = false;
            EndMutation();
            _mutationId = Guid.Empty;
        }

        public void Dispose()
        {
            EndRequest();
            Ambient.Value = _previous;
        }
    }
}
