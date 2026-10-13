// Owns login, detach, grace expiry, and retirement transitions.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Players
{
    public sealed partial class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        internal async ValueTask<SignInResult<TPlayer>> SignInAsync(PlayerSession<TPlayer> session, string accountKey)
        {
            RequireSessionSupport();
            using var admission = EnterWork();
            if (!admission.Entered) throw new InvalidOperationException("Registry admission is closed.");
            PlayerExecutionLane.ThrowIfOwned();
            if (string.IsNullOrEmpty(accountKey)) throw new ArgumentException("Player key is empty.", nameof(accountKey));
            if (session.Player != null) throw new InvalidOperationException("Session is already authenticated.");
            if (!AdmissionOpen) throw new InvalidOperationException("Registry admission is closed.");
            using var installed = PlayerExecutionLane.Current == null ? PlayerExecutionLane.Install() : null;
            var lane = PlayerExecutionLane.Current!;
            var scope = await _execution.AcquireAsync(accountKey, default).ConfigureAwait(false);
            lane.TakeOwnership(scope);
            var transferred = false;
            PlayerSession<TPlayer>? oldSession = null;
            try
            {
                if (!AdmissionOpen) throw new InvalidOperationException("Registry admission is closed.");
                var reconnect = _entries.TryGetValue(accountKey, out var entry);
                if (reconnect)
                {
                    if (entry!.Session != null && _duplicatePolicy == DuplicateLoginPolicy.RejectNew)
                        throw new DuplicateLoginException(accountKey);
                }
                else
                {
                    var loaded = await LoadPlayerAsync(accountKey).ConfigureAwait(false);
                    var player = loaded.Player;
                    if (player == null || !player.IsValid) throw new InvalidOperationException("Loader must return fresh valid player state.");
                    player.AccountKey = accountKey;
                    if (!AdmissionOpen)
                    {
                        player.IsValid = false;
                        await ReleaseAsync(player, PlayerReleaseReason.TemporaryAborted).ConfigureAwait(false);
                        throw new InvalidOperationException("Registry admission is closed.");
                    }
                    entry = new Entry(player, Interlocked.Increment(ref _generation)) { PersistentVersion = loaded.Version };
                    _execution.RetainKey(accountKey);
                    _entries[accountKey] = entry;
                    if (!AdmissionOpen)
                    {
                        _entries.TryRemove(accountKey, out _);
                        entry.Valid = false;
                        player.IsValid = false;
                        _execution.ReleaseKey(accountKey);
                        await ReleaseAsync(player, PlayerReleaseReason.TemporaryAborted).ConfigureAwait(false);
                        throw new InvalidOperationException("Registry admission is closed.");
                    }
                }
                var previousSession = entry!.Session;
                if (!TryAttach(entry, session))
                {
                    if (!reconnect)
                    {
                        _entries.TryRemove(accountKey, out _);
                        entry.Valid = false;
                        entry.Player.IsValid = false;
                        _execution.ReleaseKey(accountKey);
                        await ReleaseAsync(entry.Player, PlayerReleaseReason.TemporaryAborted).ConfigureAwait(false);
                    }
                    throw new InvalidOperationException("Cannot bind a closed session or a registry with closed admission.");
                }
                oldSession = previousSession;
                oldSession?.SetPlayer(null);
                if (_recovery.TryRemove(accountKey, out _)) _execution.ReleaseKey(accountKey);
                lane.SetMutation(entry!.Player, SupportsAudit);
                try { await entry!.Player.OnAttachedAsync(reconnect).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    await InvalidateKeyAsync(accountKey, true, ex).ConfigureAwait(false);
                    throw;
                }
                transferred = lane.InRequest && ReferenceEquals(lane.Session, session);
                return new SignInResult<TPlayer>(entry!.Player, reconnect);
            }
            finally
            {
                if (!transferred) lane.ReleaseOwnership();
                Disconnect(oldSession, DisconnectCode.DuplicateLogin, accountKey);
            }
        }

        internal async ValueTask HandleSessionClosedAsync(PlayerSession<TPlayer> session)
        {
            using var admission = EnterWork();
            if (!admission.Entered) return;
            PlayerExecutionLane.ThrowIfOwned();
            var player = session.Player;
            if (player == null) return;
            var generation = session.BindingGeneration;
            using var installed = PlayerExecutionLane.Current == null ? PlayerExecutionLane.Install() : null;
            var lane = PlayerExecutionLane.Current!;
            lane.TakeOwnership(await _execution.AcquireAsync(player.AccountKey, default).ConfigureAwait(false));
            try
            {
                if (!_entries.TryGetValue(player.AccountKey, out var entry) || !entry.Valid
                    || !ReferenceEquals(entry.Player, player) || !ReferenceEquals(entry.Session, session)
                    || session.BindingGeneration != generation) return;
                entry.Session = null;
                player.CurrentSession = null;
                session.SetPlayer(null);
                lane.SetMutation(player, SupportsAudit);
                try { await player.OnDetachedAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    await InvalidateKeyAsync(player.AccountKey, true, ex).ConfigureAwait(false);
                    return;
                }
                if (player.IsDirty) await SaveAsync(player).ConfigureAwait(false);
                if (!entry.Valid) return;
                if (_gracePeriod <= TimeSpan.Zero) await RetireEntryAsync(player.AccountKey, entry).ConfigureAwait(false);
                else entry.DetachedAtTicksUtc = DateTime.UtcNow.Ticks;
            }
            finally { lane.ReleaseOwnership(); }
        }

        /// <summary>Stops login admission and retires cached players under their execution ownership.</summary>
        /// <param name="ct">Cancellation stops waiting for and retiring remaining players.</param>
        public async ValueTask RetireAllAsync(CancellationToken ct = default)
        {
            var result = await DrainAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Player drain quiescent {IsQuiescent} unresolved {UnresolvedCount} discarded {DiscardedCount} errors {ErrorCount}",
                result.IsQuiescent, result.UnresolvedKeys.Count, result.DiscardedKeys.Count, result.Errors.Count);
            ct.ThrowIfCancellationRequested();
        }

        private async ValueTask RetireKeyAsync(string key, long? cutoff, CancellationToken ct)
        {
            var lane = PlayerExecutionLane.Current!;
            lane.TakeOwnership(await _execution.AcquireAsync(key, ct).ConfigureAwait(false));
            try
            {
                if (!_entries.TryGetValue(key, out var entry)) return;
                if (cutoff.HasValue && (entry.Session != null || entry.DetachedAtTicksUtc == 0 || entry.DetachedAtTicksUtc > cutoff.Value)) return;
                await RetireEntryAsync(key, entry).ConfigureAwait(false);
            }
            finally { lane.ReleaseOwnership(); }
        }

        private async ValueTask RetireEntryAsync(string key, Entry entry)
        {
            if (!entry.Valid) return;
            if (Volatile.Read(ref _authorityLost) != 0)
            {
                await InvalidateKeyAsync(key, false, new InvalidOperationException("Registry write authority was lost.")).ConfigureAwait(false);
                return;
            }
            PlayerExecutionLane.Current!.SetMutation(entry.Player, SupportsAudit);
            try
            {
                if (_store == null) await entry.Player.OnRetiredAsync().ConfigureAwait(false);
                else if (entry.Player.IsDirty) await SaveAsync(entry.Player).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await InvalidateKeyAsync(key, true, ex).ConfigureAwait(false);
                return;
            }
            if (!entry.Valid) return;
            _entries.TryRemove(key, out _);
            entry.Valid = false;
            entry.Player.IsValid = false;
            var session = entry.Session;
            entry.Session = null;
            entry.Player.CurrentSession = null;
            session?.SetPlayer(null);
            _execution.ReleaseKey(key);
            Disconnect(session, DisconnectCode.ServerShutdown, key);
            await ReleaseAsync(entry.Player, PlayerReleaseReason.Retired).ConfigureAwait(false);
        }

        private bool TryAttach(Entry entry, PlayerSession<TPlayer> session)
        {
            lock (_operationSync)
            {
                if (!AdmissionOpen) return false;
                if (!session.TryBindPlayer(entry.Player, Interlocked.Increment(ref _generation))) return false;
                entry.Session = session;
                entry.DetachedAtTicksUtc = 0;
                entry.Player.CurrentSession = session;
                var now = DateTime.UtcNow.Ticks;
                entry.Player.LastTickAtTicksUtc = now;
                entry.Player.NextSaveAtTicksUtc = now + _saveInterval.Ticks;
                return true;
            }
        }

        private async Task RunSweepAsync(CancellationToken ct)
        {
            using var lane = PlayerExecutionLane.Install();
            var half = TimeSpan.FromTicks(_gracePeriod.Ticks / 2);
            var floor = TimeSpan.FromMilliseconds(50);
            var ceiling = TimeSpan.FromSeconds(15);
            var interval = half < floor ? floor : half > ceiling ? ceiling : half;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    var cutoff = DateTime.UtcNow.Ticks - _gracePeriod.Ticks;
                    foreach (var pair in _entries)
                    {
                        var detached = Volatile.Read(ref pair.Value.DetachedAtTicksUtc);
                        if (detached != 0 && detached <= cutoff)
                        {
                            using var admission = EnterWork();
                            if (admission.Entered) await RetireKeyAsync(pair.Key, cutoff, ct).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Grace sweep loop failed"); }
        }
    }
}
