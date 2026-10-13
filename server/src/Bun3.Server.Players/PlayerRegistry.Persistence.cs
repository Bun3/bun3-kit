// Integrates store transactions with login and single-player lifecycle saves.
using System;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;

namespace Bun3.Server.Players
{
    public sealed partial class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        private async ValueTask<(TPlayer Player, long? Version)> LoadPlayerAsync(string key)
        {
            if (_store == null) return (await _loader!(key).ConfigureAwait(false), null);
            var diagnostics = new PlayerOperationDiagnostics(_logger, Guid.NewGuid(), "load", scopeKey: OwnedStore?.Authority.ScopeKey, instanceId: OwnedStore?.Binding.InstanceId);
            diagnostics.Record("Waiting", key);
            PlayerStoreScope<TPlayer>? scope = null;
            TPlayer? player = null;
            var completed = false;
            var phase = "Acquisition";
            try
            {
                scope = await _store.OpenScopeAsync(new[] { key }, _logger, default, PlayerStoreAccess.Load, _cleanupFailed).ConfigureAwait(false);
                phase = "AnchorVersion";
                var version = scope.GetVersion(key);
                phase = "PlayerLoad";
                player = await scope.LoadAsync(key, default).ConfigureAwait(false);
                if (player == null || !player.IsValid) throw new InvalidOperationException("Store must load fresh valid player state.");
                diagnostics.Record("Loaded", key, "temporary", expectedVersion: version);
                phase = "LoadCommit";
                var result = await scope.CommitAsync(default).ConfigureAwait(false);
                if (result.Outcome != PlayerStoreOutcome.Committed)
                    throw result.Error ?? new InvalidOperationException("Store load transaction failed.");
                diagnostics.Record(_recovery.ContainsKey(key) ? "Reloaded" : "Committed", key, expectedVersion: version, committedVersion: version);
                completed = true;
                return (player, version);
            }
            catch (PlayerStoreAcquisitionException ex)
            {
                ObserveAuthorityFailure(ex.OriginalError, ex.Phase);
                diagnostics.Record(ex.Result.Outcome == PlayerStoreOutcome.Unknown ? "CommitUnknown" : "RolledBack", key,
                    error: ex.Result.Error, phase: ex.Phase);
                ExceptionDispatchInfo.Capture(ex.OriginalError).Throw();
                throw;
            }
            catch (Exception ex)
            {
                if (scope != null)
                {
                    var result = await scope.RollbackAsync(ex).ConfigureAwait(false);
                    diagnostics.Record(result.Outcome == PlayerStoreOutcome.Unknown ? "CommitUnknown" : "RolledBack", key,
                        error: result.Error, phase: phase);
                }
                else diagnostics.Record("Rejected", key, error: ex, phase: phase);
                throw;
            }
            finally
            {
                if (!completed && player != null) await ReleaseAsync(player, PlayerReleaseReason.TemporaryAborted).ConfigureAwait(false);
                if (scope != null) await scope.DisposeAsync().ConfigureAwait(false);
            }
        }

        internal async ValueTask SaveAsync(TPlayer player)
        {
            if (System.Threading.Volatile.Read(ref _authorityLost) != 0) return;
            if (!_entries.TryGetValue(player.AccountKey, out var entry) || !entry.Valid || !ReferenceEquals(entry.Player, player)) return;
            var diagnostics = _store == null ? null : new PlayerOperationDiagnostics(_logger, PlayerExecutionLane.Current?.MutationId ?? Guid.NewGuid(), "save", scopeKey: OwnedStore?.Authority.ScopeKey, instanceId: OwnedStore?.Binding.InstanceId);
            try
            {
                if (_store == null) { await player.TrySaveAsync(_logger).ConfigureAwait(false); return; }
                var data = new PlayerSaveData(player.AccountKey, entry.PersistentVersion, player.CaptureDirtyVersion(),
                    _store.Capture(player), player.CaptureAudit());
                diagnostics!.Record("Saving", player.AccountKey, entry.Session == null ? "grace" : "online", entry.Generation, entry.PersistentVersion);
                var scope = await _store.OpenScopeAsync(new[] { player.AccountKey }, _logger, default, PlayerStoreAccess.Save, _cleanupFailed).ConfigureAwait(false);
                await using (scope.ConfigureAwait(false))
                {
                    PlayerStoreResult result;
                    try
                    {
                        await scope.WriteAsync(data, default).ConfigureAwait(false);
                        result = await scope.CommitAsync(default).ConfigureAwait(false);
                    }
                    catch (Exception ex) { result = await scope.RollbackAsync(ex).ConfigureAwait(false); }
                    if (result.Outcome != PlayerStoreOutcome.Committed)
                    {
                        diagnostics!.Record(result.Outcome == PlayerStoreOutcome.Unknown ? "CommitUnknown" : "RolledBack", player.AccountKey, error: result.Error);
                        await InvalidateKeyAsync(player.AccountKey, result.Outcome == PlayerStoreOutcome.Unknown, result.Error ?? new InvalidOperationException("Store save failed.")).ConfigureAwait(false);
                        diagnostics.Record("Invalidated", player.AccountKey, generation: entry.Generation, error: result.Error);
                        return;
                    }
                    entry.PersistentVersion = checked(data.ExpectedVersion + 1);
                    player.CompleteSave(data.CapturedDirtyVersion);
                    player.CompleteAudit(data.AuditRecords.Count);
                    diagnostics!.Record("Committed", player.AccountKey, entry.Session == null ? "grace" : "online", entry.Generation, data.ExpectedVersion, entry.PersistentVersion);
                }
            }
            catch (PlayerStoreAcquisitionException ex)
            {
                ObserveAuthorityFailure(ex.OriginalError, ex.Phase);
                diagnostics?.Record(ex.Result.Outcome == PlayerStoreOutcome.Unknown ? "CommitUnknown" : "RolledBack", player.AccountKey,
                    error: ex.Result.Error, phase: ex.Phase);
                await InvalidateKeyAsync(player.AccountKey, true, ex.Result.Error ?? ex.OriginalError).ConfigureAwait(false);
                diagnostics?.Record("Invalidated", player.AccountKey, generation: entry.Generation, error: ex.Result.Error);
            }
            catch (Exception ex)
            {
                diagnostics?.Record("Rejected", player.AccountKey, error: ex, phase: "Capture");
                await InvalidateKeyAsync(player.AccountKey, true, ex).ConfigureAwait(false);
                diagnostics?.Record("Invalidated", player.AccountKey, generation: entry.Generation, error: ex);
            }
        }
    }
}
