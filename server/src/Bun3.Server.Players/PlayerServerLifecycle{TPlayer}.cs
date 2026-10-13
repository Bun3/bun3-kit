using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    /// <summary>Coordinates a server's startup and shutdown with its durable write authority.</summary>
    public sealed class PlayerServerLifecycle<TPlayer> where TPlayer : Player
    {
        private readonly PlayerRegistry<TPlayer> _registry;
        private readonly OwnedPlayerStore<TPlayer> _store;
        private readonly PlayerWriteTransition _prepare;
        private readonly PlayerWriteTransition _activate;
        private readonly PlayerWriteTransition _drain;
        private readonly PlayerWriteTransition _offline;
        /// <summary>Creates a lifecycle coordinator for a registry bound to a server instance.</summary>
        public PlayerServerLifecycle(PlayerRegistry<TPlayer> registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _store = registry.OwnedStore ?? throw new ArgumentException("Authority-protected persistence is required.", nameof(registry));
            if (_store.Binding.Role != PlayerWriteRole.Server) throw new ArgumentException("A server binding is required.", nameof(registry));
            var id = _store.Binding.InstanceId; var actor = id.ToString("D");
            _prepare = new PlayerWriteTransition(Guid.NewGuid(), PlayerWriteState.Offline, null, PlayerWriteState.Starting, id, actor, "Server starting");
            _activate = new PlayerWriteTransition(Guid.NewGuid(), PlayerWriteState.Starting, id, PlayerWriteState.Online, id, actor, "Server ready");
            _drain = new PlayerWriteTransition(Guid.NewGuid(), PlayerWriteState.Online, id, PlayerWriteState.Draining, id, actor, "Server draining");
            _offline = new PlayerWriteTransition(Guid.NewGuid(), PlayerWriteState.Draining, id, PlayerWriteState.Offline, null, actor, "Server stopped");
        }
        /// <summary>Claims Offline for this server before initialization.</summary>
        public ValueTask<PlayerWriteTransitionResult> PrepareAsync(CancellationToken ct = default) => _store.Authority.TransitionAsync(_prepare, ct);
        /// <summary>Confirms Online and opens local admission after successful initialization.</summary>
        public async ValueTask<PlayerWriteTransitionResult> ActivateAsync(CancellationToken ct = default)
        {
            var result = await _store.Authority.TransitionAsync(_activate, ct).ConfigureAwait(false);
            if (result.Outcome == PlayerWriteTransitionOutcome.Committed) _registry.OpenAuthorityAdmission();
            return result;
        }
        /// <summary>Closes all local intake before requesting Draining.</summary>
        public ValueTask<PlayerWriteTransitionResult> BeginDrainAsync(CancellationToken ct = default)
        { _registry.StopAcceptingOperations(); return _store.Authority.TransitionAsync(_drain, ct); }
        /// <summary>Publishes Offline only after all local writers and uncertain outcomes have been resolved.</summary>
        public async ValueTask<PlayerServerShutdownResult> CompleteDrainAsync(CancellationToken ct = default)
        {
            var drain = await _registry.DrainAsync(ct).ConfigureAwait(false);
            var transition = drain.IsQuiescent ? await _store.Authority.TransitionAsync(_offline, ct).ConfigureAwait(false) : null;
            return new PlayerServerShutdownResult(drain, transition);
        }
    }
}
