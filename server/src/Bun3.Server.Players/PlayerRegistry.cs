using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Players
{
    /// <summary>
    /// accountKey → Player registry. In-process memory only (multi-server scale-out is a
    /// separate design). Per-account-key serialization uses 256 stripe locks.
    /// </summary>
    public sealed class PlayerRegistry<TPlayer> : IDisposable where TPlayer : Player
    {
        private const int StripeCount = 256;

        private sealed class Entry
        {
            public readonly TPlayer Player;
            public PlayerSession<TPlayer>? Session;
            public long DetachedAtTicksUtc;   // 0 = connected

            public Entry(TPlayer player) => Player = player;
        }

        private readonly Func<string, ValueTask<TPlayer>> _loader;
        private readonly TimeSpan _gracePeriod;
        private readonly TimeSpan _saveInterval;
        private readonly DuplicateLoginPolicy _duplicatePolicy;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>();
        private readonly SemaphoreSlim[] _stripes;
        private readonly CancellationTokenSource _sweepCts = new CancellationTokenSource();
        private volatile bool _retired;
        private int _disposed;

        /// <summary>
        /// Creates the registry from the account-key loader, options, and logger. When
        /// GracePeriod &gt; 0, the background grace sweep loop starts immediately.
        /// </summary>
        public PlayerRegistry(
            Func<string, ValueTask<TPlayer>> loader,
            PlayersOptions? options = null,
            ILogger? logger = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            var effectiveOptions = options ?? new PlayersOptions();
            _gracePeriod = effectiveOptions.GracePeriod;   // snapshot at construction — later option mutation is ignored
            _saveInterval = effectiveOptions.SaveInterval;
            _duplicatePolicy = effectiveOptions.DuplicatePolicy;
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
            _stripes = new SemaphoreSlim[StripeCount];
            for (var i = 0; i < StripeCount; i++)
            {
                _stripes[i] = new SemaphoreSlim(1, 1);
            }

            if (_gracePeriod > TimeSpan.Zero)
            {
                _ = RunSweepAsync(_sweepCts.Token);
            }
        }

        /// <summary>Snapshot of current players (for broadcast). Allocates an array per
        /// call — periodic iteration should use <see cref="ForEachPlayer"/>.</summary>
        public IReadOnlyCollection<TPlayer> Players => _entries.Values.Select(e => e.Player).ToArray();

        /// <summary>Allocation-free iteration — the ConcurrentDictionary enumerator iterates lock-free
        /// without a snapshot (concurrent add/remove is safe; visibility is unspecified). For PlayerTicker's tick path.</summary>
        internal void ForEachPlayer(Action<TPlayer> action)
        {
            foreach (var pair in _entries)
            {
                action(pair.Value.Player);
            }
        }

        /// <summary>Looks up by accountKey; null when absent.</summary>
        public TPlayer? TryGet(string accountKey) =>
            _entries.TryGetValue(accountKey, out var entry) ? entry.Player : null;

        /// <summary>Wraps the session factory to attach the registry and allowlist. Required path for using Players.</summary>
        public Func<IConnection, TSession> Wrap<TSession>(
            PlayersConfig<TSession> config, Func<IConnection, TSession> factory)
            where TSession : PlayerSession<TPlayer>
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var unauthenticatedTypes = new HashSet<Type>(config.UnauthenticatedTypes);   // snapshot at validation time — late registrations cannot silently open the gate
            return connection =>
            {
                var session = factory(connection);
                session.AttachPlayers(this, unauthenticatedTypes);
                return session;
            };
        }

        internal async ValueTask<SignInResult<TPlayer>> SignInAsync(
            PlayerSession<TPlayer> session, string accountKey)
        {
            if (string.IsNullOrEmpty(accountKey))
            {
                throw new ArgumentException("accountKey is empty.", nameof(accountKey));
            }

            if (session.Player != null)
            {
                throw new InvalidOperationException("SignInAsync called again on an already authenticated session.");
            }

            if (_retired)
            {
                throw new InvalidOperationException("Registry retired (server shutting down) — SignIn unavailable.");
            }

            PlayerSession<TPlayer>? kickAfterRelease = null;
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                // Re-check inside the lock — RetireAll can slip in between the fast check above and here (while waiting on the stripe).
                if (_retired)
                {
                    throw new InvalidOperationException("Registry retired (server shutting down) — SignIn unavailable.");
                }

                if (_entries.TryGetValue(accountKey, out var entry))
                {
                    if (entry.Session != null && _duplicatePolicy == DuplicateLoginPolicy.RejectNew)
                    {
                        throw new DuplicateLoginException(accountKey);
                    }

                    kickAfterRelease = entry.Session;   // NewWins: kick after lock release (avoids reentrant deadlock)
                    // De-authorize the old session immediately — until the kick completes, requests still
                    // queued on it are blocked at the gate (Unauthenticated) and cannot touch the transferred Player.
                    // One handler already executing cannot be preempted — if it re-reads Player it sees
                    // null and fails, and the session is being kicked anyway.
                    kickAfterRelease?.SetPlayer(null);
                    entry.DetachedAtTicksUtc = 0;
                    Attach(entry, session);
                    await SafeHookAsync(() => entry.Player.OnAttachedAsync(true), "OnAttachedAsync").ConfigureAwait(false);
                    return new SignInResult<TPlayer>(entry.Player, true);
                }

                // ponytail: DB load inside the stripe lock — other keys on the same stripe wait for
                // the load duration (rare with 256 stripes). Promote to per-key locks if a bottleneck is measured.
                var player = await _loader(accountKey).ConfigureAwait(false);

                // RetireAll may have finished while the loader was slow — re-check just before insert to prevent an orphan entry.
                if (_retired)
                {
                    throw new InvalidOperationException("Registry retired (server shutting down) — SignIn unavailable.");
                }

                player.AccountKey = accountKey;
                var created = new Entry(player);
                _entries[accountKey] = created;

                // Re-check right after insert — if RetireAll took its snapshot and completed between
                // the check above and the insert, this entry is missing from reclamation; the inserter
                // rolls itself back to prevent the orphan. (If _retired was false here, the insert became
                // visible before RetireAll's snapshot — guaranteed by ConcurrentDictionary's insert/enumeration lock fences.)
                if (_retired)
                {
                    _entries.TryRemove(accountKey, out _);
                    throw new InvalidOperationException("Registry retired (server shutting down) — SignIn unavailable.");
                }

                Attach(created, session);
                await SafeHookAsync(() => player.OnAttachedAsync(false), "OnAttachedAsync").ConfigureAwait(false);
                return new SignInResult<TPlayer>(player, false);
            }
            finally
            {
                stripe.Release();
                kickAfterRelease?.Kick(DisconnectCode.DuplicateLogin);
            }
        }

        internal async ValueTask HandleSessionClosedAsync(PlayerSession<TPlayer> session)
        {
            var player = session.Player;
            if (player == null)
            {
                return;   // unauthenticated session
            }

            var accountKey = player.AccountKey;
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_entries.TryGetValue(accountKey, out var entry)
                    || !ReferenceEquals(entry.Session, session))
                {
                    return;   // already rebound to another session (duplicate login) or retired
                }

                entry.Session = null;
                player.CurrentSession = null;
                await SafeHookAsync(() => player.OnDetachedAsync(), "OnDetachedAsync").ConfigureAwait(false);

                if (player.IsDirty)
                {
                    await player.TrySaveAsync(_logger).ConfigureAwait(false);   // save on detach → during grace the state is always saved
                }

                if (_gracePeriod <= TimeSpan.Zero)
                {
                    _entries.TryRemove(accountKey, out _);
                    await SafeHookAsync(() => player.OnRetiredAsync(), "OnRetiredAsync").ConfigureAwait(false);
                }
                else
                {
                    entry.DetachedAtTicksUtc = DateTime.UtcNow.Ticks;
                }
            }
            finally
            {
                stripe.Release();
            }
        }

        /// <summary>Retires all players (save flush) — call after stopping the server. Also stops the sweep.</summary>
        /// <param name="ct">Host shutdown deadline — cancellation aborts retirement of remaining keys (already processed keys are complete).</param>
        public async ValueTask RetireAllAsync(CancellationToken ct = default)
        {
            _retired = true;
            try
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    _sweepCts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // race with Dispose — the sweep is already stopped
            }

            // A single pass suffices: inserters re-check _retired inside the stripe before and after
            // insert, and a late insert that missed the snapshot rolls itself back (no orphans possible).
            // RetireAll does not wait for in-flight loaders — shutdown is never held hostage by a slow DB load.
            foreach (var accountKey in _entries.Keys.ToArray())
            {
                ct.ThrowIfCancellationRequested();
                await RetireKeyAsync(accountKey).ConfigureAwait(false);
            }
        }

        private async ValueTask RetireKeyAsync(string accountKey)
        {
            PlayerSession<TPlayer>? toKick = null;
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_entries.TryRemove(accountKey, out var entry))
                {
                    toKick = entry.Session;
                    entry.Player.CurrentSession = null;
                    await SafeHookAsync(() => entry.Player.OnRetiredAsync(), "OnRetiredAsync").ConfigureAwait(false);
                }
            }
            finally
            {
                stripe.Release();
                toKick?.Kick(DisconnectCode.ServerShutdown);   // reason is delivered even when RetireAll is called directly outside hosting
            }
        }

        private void Attach(Entry entry, PlayerSession<TPlayer> session)
        {
            entry.Session = session;
            entry.Player.CurrentSession = session;
            var now = DateTime.UtcNow.Ticks;
            entry.Player.LastTickAtTicksUtc = now;                          // delta reset — offline span not accumulated
            entry.Player.NextSaveAtTicksUtc = now + _saveInterval.Ticks;    // re-arm the save interval
            session.SetPlayer(entry.Player);
        }

        private SemaphoreSlim GetStripe(string accountKey) =>
            _stripes[(accountKey.GetHashCode() & int.MaxValue) % StripeCount];

        private async ValueTask SafeHookAsync(Func<ValueTask> hook, string name)
        {
            try
            {
                await hook().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Player hook {Hook} exception", name);
            }
        }

        private async Task RunSweepAsync(CancellationToken ct)
        {
            var half = TimeSpan.FromTicks(_gracePeriod.Ticks / 2);
            var floor = TimeSpan.FromMilliseconds(50);
            var ceiling = TimeSpan.FromSeconds(15);
            var interval = half < floor ? floor : (half > ceiling ? ceiling : half);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    var cutoff = DateTime.UtcNow.Ticks - _gracePeriod.Ticks;
                    foreach (var pair in _entries)
                    {
                        var detachedAt = Volatile.Read(ref pair.Value.DetachedAtTicksUtc);
                        if (detachedAt == 0 || detachedAt > cutoff)
                        {
                            continue;
                        }

                        await RetireIfStillExpiredAsync(pair.Key, cutoff).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal cancellation from RetireAll/shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Grace sweep loop exception — sweep stopped");
            }
        }

        private async ValueTask RetireIfStillExpiredAsync(string accountKey, long cutoff)
        {
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_entries.TryGetValue(accountKey, out var entry)
                    && entry.Session == null
                    && entry.DetachedAtTicksUtc != 0
                    && entry.DetachedAtTicksUtc <= cutoff)
                {
                    _entries.TryRemove(accountKey, out _);
                    await SafeHookAsync(() => entry.Player.OnRetiredAsync(), "OnRetiredAsync").ConfigureAwait(false);
                }
            }
            finally
            {
                stripe.Release();
            }
        }

        /// <summary>Stops the grace sweep and cleans up internals. Idempotent.
        /// This is NOT retirement — it does not call save hooks. For graceful shutdown call RetireAllAsync first.
        /// (Dispose is for tests / abnormal cleanup.)</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _sweepCts.Cancel();
            _sweepCts.Dispose();
        }
    }
}
