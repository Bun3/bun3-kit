// Coordinates authority failure with registry admission and cache disposal.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    public sealed partial class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        private int _authorityLost;
        private Task _authorityCleanup = Task.CompletedTask;
        internal Task AuthorityCleanup => _authorityCleanup;
        internal OwnedPlayerStore<TPlayer>? OwnedStore => _store as OwnedPlayerStore<TPlayer>;
        internal bool IsOfflineExecutor => OwnedStore?.Binding.Role == PlayerWriteRole.OfflineAdministrator;
        internal void RequireSessionSupport()
        {
            if (IsOfflineExecutor) throw new InvalidOperationException("Offline command executors do not support sessions or ticking.");
        }
        private void ObserveAuthorityFailure(Exception error, string? phase = null)
        {
            if (OwnedStore?.Binding.Role != PlayerWriteRole.Server || error is OperationCanceledException) return;
            if (!(error is PlayerWriteAuthorityException) && (phase == null || phase == "AnchorLock")) return;
            if (Interlocked.Exchange(ref _authorityLost, 1) != 0) return;
            StopAcceptingOperations();
            if (ExecutionContext.IsFlowSuppressed()) _authorityCleanup = Task.Run(() => DiscardAuthorityCacheAsync(error));
            else
            {
                using (ExecutionContext.SuppressFlow()) _authorityCleanup = Task.Run(() => DiscardAuthorityCacheAsync(error));
            }
        }
        private async Task DiscardAuthorityCacheAsync(Exception error)
        {
            using var installed = PlayerExecutionLane.Install();
            var lane = PlayerExecutionLane.Current!;
            foreach (var pair in _entries)
            {
                lane.TakeOwnership(await _execution.AcquireAsync(pair.Key, default).ConfigureAwait(false));
                try { await InvalidateKeyAsync(pair.Key, false, error).ConfigureAwait(false); }
                finally { lane.ReleaseOwnership(); }
            }
        }
    }
}
