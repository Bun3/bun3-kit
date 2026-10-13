using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    /// <summary>Runs accepted commands using the registry that currently owns their write scope.</summary>
    public sealed class PlayerCommandDispatcher<TPlayer> where TPlayer : Player
    {
        private readonly PlayerCommandQueue _queue;
        private readonly PlayerCommandCatalog<TPlayer> _catalog;
        private readonly PlayerRegistry<TPlayer> _registry;
        /// <summary>Binds the queue and frozen catalog to an authority-protected registry.</summary>
        public PlayerCommandDispatcher(PlayerCommandQueue queue, PlayerCommandCatalog<TPlayer> catalog, PlayerRegistry<TPlayer> registry)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue)); _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (registry.OwnedStore == null) throw new ArgumentException("Command dispatch requires authority-protected persistence.", nameof(registry));
            catalog.Freeze();
        }
        /// <summary>Attempts one pending command. Unsupported or currently unauthorized commands remain pending.</summary>
        public async ValueTask<PlayerCommandRecord?> ExecuteAsync(Guid commandId, CancellationToken ct = default)
        {
            var record = await _queue.GetAsync(commandId, ct).ConfigureAwait(false);
            if (record == null || record.IsTerminal || record.Envelope.ScopeKey != _registry.OwnedStore!.Authority.ScopeKey) return record;
            var registration = _catalog.Find(record.Envelope);
            if (registration == null || (_registry.IsOfflineExecutor && !registration.SupportsOffline)) return record;
            return await registration.Execute(_registry, record.Envelope, _queue.Store, ct).ConfigureAwait(false) ?? record;
        }
    }
}
