// Provides registry configuration, cached player lookup, and factory binding.
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
    /// separate design). Execution, lifecycle, persistence, operations, and invalidation partials share per-key ownership.
    /// </summary>
    public sealed partial class PlayerRegistry<TPlayer> : IDisposable where TPlayer : Player
    {
        private sealed class Entry
        {
            public readonly TPlayer Player;
            public PlayerSession<TPlayer>? Session;
            public long DetachedAtTicksUtc;   // 0 = connected
            public long? PersistentVersion;
            public long Generation;
            public bool Valid = true;

            public Entry(TPlayer player, long generation)
            {
                Player = player;
                Generation = generation;
                PersistentVersion = null;
            }
        }

        private readonly Func<string, ValueTask<TPlayer>>? _loader;
        private readonly PlayerPersistence<TPlayer>? _store;
        private readonly TimeSpan _gracePeriod;
        private readonly TimeSpan _saveInterval;
        private readonly DuplicateLoginPolicy _duplicatePolicy;
        private readonly ILogger _logger;
        private readonly int _maxOperationParticipants;
        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>();
        private readonly PlayerExecutionTable _execution = new PlayerExecutionTable();
        private readonly ConcurrentDictionary<string, byte> _recovery = new ConcurrentDictionary<string, byte>();
        private long _generation;
        private readonly CancellationTokenSource _sweepCts = new CancellationTokenSource();
        private readonly Task _sweepTask = Task.CompletedTask;
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
            : this(loader ?? throw new ArgumentNullException(nameof(loader)), null, options, logger) { }

        /// <summary>Creates a registry whose loads and saves use framework-owned store transactions.</summary>
        public PlayerRegistry(PlayerStore<TPlayer> store, PlayersOptions? options = null, ILogger? logger = null)
            : this(null, store ?? throw new ArgumentNullException(nameof(store)), options, logger) { }

        /// <summary>Creates a registry using versioned or authority-protected persistence.</summary>
        public PlayerRegistry(PlayerPersistence<TPlayer> store, PlayersOptions? options = null, ILogger? logger = null)
            : this(null, store ?? throw new ArgumentNullException(nameof(store)), options, logger) { }

        private PlayerRegistry(Func<string, ValueTask<TPlayer>>? loader, PlayerPersistence<TPlayer>? store,
            PlayersOptions? options, ILogger? logger)
        {
            _loader = loader;
            _store = store;
            var effectiveOptions = options ?? new PlayersOptions();
            if (effectiveOptions.MaxOperationParticipants < 1) throw new ArgumentOutOfRangeException(nameof(options));
            _maxOperationParticipants = effectiveOptions.MaxOperationParticipants;
            _gracePeriod = effectiveOptions.GracePeriod;   // snapshot at construction — later option mutation is ignored
            _saveInterval = effectiveOptions.SaveInterval;
            _duplicatePolicy = effectiveOptions.DuplicatePolicy;
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
            _cleanupFailed = RecordCleanupFailure;
            if (OwnedStore?.Binding.Role == PlayerWriteRole.Server) _admission.Close();

            if (_gracePeriod > TimeSpan.Zero && !IsOfflineExecutor)
            {
                _sweepTask = RunSweepAsync(_sweepCts.Token);
            }
        }

        /// <summary>Snapshot of current players (for broadcast). Allocates an array per
        /// call — periodic iteration should use <see cref="ForEachPlayer"/>.</summary>
        public IReadOnlyCollection<TPlayer> Players => _entries.Values.Select(e => e.Player).ToArray();

        /// <summary>Gets the durable write binding, or null for loader and versioned persistence.</summary>
        public PlayerWriteBinding? WriteBinding => OwnedStore?.Binding;

        /// <summary>Gets the write authority coordinator, or null for loader and versioned persistence.</summary>
        public PlayerWriteAuthority? WriteAuthority => OwnedStore?.Authority;

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
            RequireSessionSupport();
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var operationTypes = new HashSet<Type>();
            foreach (var registration in config.OperationTypes)
            {
                if (_store == null || registration.Value != typeof(TPlayer))
                    throw new InvalidOperationException("Operation registrations require a store and the registry's exact player type.");
                operationTypes.Add(registration.Key);
            }
            var unauthenticatedTypes = new HashSet<Type>(config.UnauthenticatedTypes);   // snapshot at validation time — late registrations cannot silently open the gate
            return connection =>
            {
                var session = factory(connection);
                session.AttachPlayers(this, unauthenticatedTypes, operationTypes);
                return session;
            };
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

            StopAcceptingOperations();

            _sweepCts.Cancel();
            _sweepCts.Dispose();
        }
    }
}
