// Reports registry cleanup before durable write ownership is released.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    public sealed partial class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        private readonly SemaphoreSlim _shutdown = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<Exception> _cleanupErrors = new ConcurrentQueue<Exception>();
        private readonly ConcurrentDictionary<string, byte> _discarded = new ConcurrentDictionary<string, byte>();
        private readonly Action<Exception> _cleanupFailed;
        private void RecordCleanupFailure(Exception error) => _cleanupErrors.Enqueue(error);
        /// <summary>Closes admission, completes active work and reports final persistence and cleanup outcomes.</summary>
        public async ValueTask<PlayerDrainResult> DrainAsync(CancellationToken ct = default)
        {
            PlayerExecutionLane.ThrowIfOwned();
            if (PlayerExecutionLane.Current?.InRequest == true) throw new InvalidOperationException("A request cannot drain its own registry.");
            StopAcceptingOperations();
            await _shutdown.WaitAsync(ct).ConfigureAwait(false);
            var errors = new List<Exception>();
            try
            {
                _retired = true;
                try { _sweepCts.Cancel(); } catch (ObjectDisposedException) { }
                await StopAndDrainOperationsAsync(ct).ConfigureAwait(false);
                await PlayerAdmissionGate.WaitAsync(_sweepTask, ct).ConfigureAwait(false);
                await PlayerAdmissionGate.WaitAsync(AuthorityCleanup, ct).ConfigureAwait(false);
                using var installed = PlayerExecutionLane.Install();
                foreach (var key in _entries.Keys)
                {
                    ct.ThrowIfCancellationRequested();
                    await RetireKeyAsync(key, null, ct).ConfigureAwait(false);
                }
                await PlayerAdmissionGate.WaitAsync(AuthorityCleanup, ct).ConfigureAwait(false);
                foreach (var key in _recovery.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    var lane = PlayerExecutionLane.Current!;
                    lane.TakeOwnership(await _execution.AcquireAsync(key, ct).ConfigureAwait(false));
                    try
                    {
                        if (_store == null) continue;
                        await using var scope = await _store.OpenScopeAsync(new[] { key }, _logger, ct, PlayerStoreAccess.Recovery, _cleanupFailed).ConfigureAwait(false);
                        if (!scope.GetAnchor(key).Exists) throw new PlayerAnchorNotFoundException(key);
                        if (_recovery.TryRemove(key, out _)) _execution.ReleaseKey(key);
                    }
                    catch (Exception ex) { errors.Add(ex); }
                    finally { lane.ReleaseOwnership(); }
                }
            }
            catch (Exception ex) { errors.Add(ex); }
            finally { _shutdown.Release(); }
            errors.AddRange(_cleanupErrors);
            var unresolved = _recovery.Keys.Concat(_entries.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            return new PlayerDrainResult(_admission.Active == 0 && _entries.IsEmpty && _sweepTask.IsCompleted && AuthorityCleanup.IsCompletedSuccessfully &&
                unresolved.Length == 0 && errors.Count == 0, Array.AsReadOnly(unresolved), Array.AsReadOnly(_discarded.Keys.ToArray()), errors.AsReadOnly());
        }
    }
}
