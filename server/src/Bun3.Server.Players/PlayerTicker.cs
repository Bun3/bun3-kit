using System;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Ticking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Players
{
    /// <summary>
    /// TickLoop job driving tick/periodic save for connected players. Iteration (tick loop thread)
    /// only posts; execution happens inside each Player's current session actor (zero locks).
    /// Players in grace are skipped — they were saved immediately on detach, so they are always saved.
    /// Hosting (AddPlayerServer) wires this automatically; non-hosting calls Register directly.
    /// </summary>
    public sealed class PlayerTicker<TPlayer> where TPlayer : Player
    {
        private readonly PlayerRegistry<TPlayer> _registry;
        private readonly TimeSpan _tickInterval;
        private readonly TimeSpan _saveInterval;
        private readonly ILogger _logger;
        private readonly Action<TPlayer> _tickPlayer;   // cached to avoid a per-tick delegate allocation

        /// <summary>Configures the ticker from the registry and options. Options are snapshotted.</summary>
        public PlayerTicker(PlayerRegistry<TPlayer> registry, PlayersOptions? options = null, ILogger? logger = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            var effective = options ?? new PlayersOptions();
            _tickInterval = effective.PlayerTickInterval;
            _saveInterval = effective.SaveInterval;
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
            _tickPlayer = TickPlayer;
        }

        /// <summary>Registers the player tick job on the tick loop. Call before loop.Start.</summary>
        public void Register(TickLoop loop)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));
            loop.Every(_tickInterval, TickAsync, "players");
        }

        internal ValueTask TickAsync(TimeSpan _)
        {
            _registry.ForEachPlayer(_tickPlayer);   // allocation-free iteration — concurrent add/remove safe
            return default;
        }

        private void TickPlayer(TPlayer player)
        {
            var session = player.CurrentSession;
            if (session == null)
            {
                return;   // in grace — no tick
            }

            // The tick work closure is cached per (player, session) pair — recreated only on rebinding.
            // The cache fields are touched only by the tick loop thread, so no lock is needed.
            var work = player.TickWork;
            if (work == null || !ReferenceEquals(player.TickWorkSession, session))
            {
                work = CreateTickWork(player, session);
                player.TickWork = work;
                player.TickWorkSession = session;
            }

            if (!session.Post(work))
            {
                // Closing or queue full — skip this tick, the next one will come (shutdown races are normal, hence Debug).
                _logger.LogDebug("Player {AccountKey}: tick skipped, session queue full or closing", player.AccountKey);
            }
        }

        private Func<ValueTask> CreateTickWork(TPlayer player, RpcSession session) => async () =>
        {
            if (!ReferenceEquals(player.CurrentSession, session))
            {
                return;   // re-check at execution time — guards against NewWins transfer/kick races
            }

            var now = DateTime.UtcNow.Ticks;
            var delta = TimeSpan.FromTicks(Math.Max(0, now - player.LastTickAtTicksUtc));
            player.LastTickAtTicksUtc = now;
            try
            {
                await player.OnTickAsync(delta).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnTickAsync exception (Player {AccountKey})", player.AccountKey);
            }

            if (!ReferenceEquals(player.CurrentSession, session))
            {
                return;   // ownership transferred (NewWins) during OnTickAsync — the new session's sweep handles saving (stays dirty)
            }

            if (now >= player.NextSaveAtTicksUtc && player.IsDirty)
            {
                player.NextSaveAtTicksUtc = now + _saveInterval.Ticks;
                await player.TrySaveAsync(_logger).ConfigureAwait(false);
            }
        };
    }
}
