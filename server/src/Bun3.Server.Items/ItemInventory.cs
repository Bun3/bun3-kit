using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;
using Bun3.Server.Core;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Unified stack/instance inventory — the single player item container. Currencies are
    /// handled as item rows of stackable definitions. The stackable/unstackable decision is
    /// made exactly once internally from catalog metadata: stackables auto-merge into a
    /// singleton instance per definition, unstackables are N amount-1 instances. Amounts are
    /// <see cref="BigNum"/> (long converts implicitly).
    /// All mutating operations are atomic; on failure the inventory is fully unchanged.
    /// No locks (single-threaded session-actor contract).
    /// Query, enumeration, and consume paths are allocation-free; only instance creation
    /// allocates (low frequency).
    /// File layout: this file (construction, queries) / Operations (grant, consume, transactions) /
    /// Crafting / Rewards / Regen / Transfer / Tracking (change tracking, load).
    /// </summary>
    /// <typeparam name="TState">Game-defined instance state type.</typeparam>
    public sealed partial class ItemInventory<TState>
    {
        // ponytail: cap on instances created per operation for unstackables — prevents a runaway
        // creation loop when mass-granting into an unlimited-maxCount definition. Promote to an
        // option if legitimate mass grants become necessary.
        internal const int MaxInstancesPerOperation = 1000;

        private readonly ItemCatalog _catalog;
        private readonly Func<long> _instanceIdIssuer;
        private readonly Func<ItemId, TState> _stateFactory;
        private readonly Action? _onChanged;
        private readonly uint _removeBlockingFlags;
        private readonly Dictionary<long, ItemInstance<TState>> _instances;
        private readonly Dictionary<ItemId, long> _stackSingletons;
        private readonly List<long> _removed = new List<long>();
        private readonly List<ItemInstance<TState>> _removeScratch = new List<ItemInstance<TState>>();
        private bool _hasChanges;

        // Transaction scratch — allocated once at construction, reused (commit path is allocation-free).
        private readonly List<TxOp> _txOps = new List<TxOp>();
        private readonly List<TxOp> _applyOps = new List<TxOp>();
        private readonly List<long> _txUnstackableTargets = new List<long>();
        private readonly List<long> _txConsumedTargets = new List<long>();
        private readonly List<BigNum> _txResolved = new List<BigNum>();
        private readonly List<ItemId> _txNetIds = new List<ItemId>();
        private readonly List<BigNum> _txNetTotal = new List<BigNum>();
        private readonly List<BigNum> _txNetPool = new List<BigNum>();
        private readonly List<ItemDelta> _rewardScratch = new List<ItemDelta>();
        private readonly Dictionary<ItemId, long> _regenBasis = new Dictionary<ItemId, long>();
        private readonly List<ItemId> _regenScratchItems = new List<ItemId>();
        private readonly List<long> _regenScratchBasis = new List<long>();
        private int _txToken;

        // Reusable buffer for onApplied notifications — recording is skipped entirely when no handler is set.
        private readonly InventoryAppliedHandler? _onApplied;
        private InventoryChange[] _applied = Array.Empty<InventoryChange>();
        private int _appliedCount;

        // Action log (audit ledger) — session-wide ActionLog reference. Recording is a no-op when unset.
        private readonly ActionLog? _log;
        private readonly string? _logLabel;

        /// <summary>Creates the inventory.</summary>
        /// <param name="catalog">Item catalog.</param>
        /// <param name="instanceIdIssuer">Instance id issuer — must be unique across sessions
        /// (snowflake, hi-lo, DB sequence, etc. is the game's choice). Called only after validation passes.</param>
        /// <param name="stateFactory">Initial state factory for created instances.</param>
        /// <param name="capacity">Initial capacity (0 = default).</param>
        /// <param name="onChanged">Called once per successful mutating operation plus once per
        /// <see cref="ItemInstance{TState}.MarkChanged"/> — games pass Player.MarkDirty to tie
        /// into the save cadence.</param>
        /// <param name="removeBlockingFlags">Instances with flags matching this mask are excluded
        /// as consume candidates (e.g. in use, user-locked). 0 = no locking.</param>
        /// <param name="onApplied">Called once per successful commit with the applied net deltas —
        /// for achievement/quest/ranking counting. No recording cost when unset.</param>
        /// <param name="log">Session-wide action log (<see cref="ActionLog"/>) — when set, every
        /// change of this inventory (delta and balance) is auto-attached to the currently open scope.
        /// Multiple inventories may share one log to land in a single action tree.</param>
        /// <param name="logLabel">Label distinguishing the change source when inventories share a log
        /// (e.g. "bag"/"warehouse").</param>
        public ItemInventory(
            ItemCatalog catalog,
            Func<long> instanceIdIssuer,
            Func<ItemId, TState> stateFactory,
            int capacity = 0,
            Action? onChanged = null,
            uint removeBlockingFlags = 0,
            InventoryAppliedHandler? onApplied = null,
            ActionLog? log = null,
            string? logLabel = null)
        {
            _onApplied = onApplied;
            _log = log;
            _logLabel = logLabel;
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _instanceIdIssuer = instanceIdIssuer ?? throw new ArgumentNullException(nameof(instanceIdIssuer));
            _stateFactory = stateFactory ?? throw new ArgumentNullException(nameof(stateFactory));
            _onChanged = onChanged;
            _removeBlockingFlags = removeBlockingFlags;
            _instances = new Dictionary<long, ItemInstance<TState>>(capacity);
            _stackSingletons = new Dictionary<ItemId, long>();
        }

        /// <summary>The catalog this inventory is bound to.</summary>
        public ItemCatalog Catalog => _catalog;

        /// <summary>Number of held instances (including stack singletons).</summary>
        public int InstanceCount => _instances.Count;

        /// <summary>Total held amount for a definition — singleton amount for stackables,
        /// instance count for unstackables. 0 if not held.</summary>
        public BigNum GetQuantity(ItemId item)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                return _instances[singletonId].Quantity;
            }

            long count = 0;
            // ponytail: full scan without a per-definition index (O(instance count)) — assumes
            // player inventories in the hundreds.
            foreach (var entry in _instances)
            {
                if (entry.Value.Item == item)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Looks up an instance by id.</summary>
        public bool TryGetInstance(long instanceId, out ItemInstance<TState> instance)
        {
            if (_instances.TryGetValue(instanceId, out var found))
            {
                instance = found;
                return true;
            }

            instance = null!;
            return false;
        }

        /// <summary>Appends the definition's held instances to the buffer and returns the count added.</summary>
        public int CollectInstances(ItemId item, List<ItemInstance<TState>> buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var count = 0;
            foreach (var entry in _instances)
            {
                if (entry.Value.Item == item)
                {
                    buffer.Add(entry.Value);
                    count++;
                }
            }

            return count;
        }

        /// <summary>Appends expired instances (<see cref="ItemInstance{TState}.ExpiresAtTicksUtc"/>
        /// non-zero and at most <paramref name="nowTicksUtc"/>) to the buffer and returns the count
        /// added. Collection only — expiry handling (consume, follow-up reward, idempotency marker)
        /// is the game's job, and time is always game-injected (determinism, testability).</summary>
        public int CollectExpired(long nowTicksUtc, List<ItemInstance<TState>> buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var count = 0;
            foreach (var entry in _instances)
            {
                var expiresAt = entry.Value.ExpiresAtTicksUtc;
                if (expiresAt != 0 && expiresAt <= nowTicksUtc)
                {
                    buffer.Add(entry.Value);
                    count++;
                }
            }

            return count;
        }

        /// <summary>Enumerates held instances — allocation-free foreach (struct enumerator).</summary>
        public Enumerator GetEnumerator() => new Enumerator(_instances.Values.GetEnumerator());

        /// <summary>Instance enumerator wrapping the dictionary value struct enumerator.</summary>
        public struct Enumerator
        {
            private Dictionary<long, ItemInstance<TState>>.ValueCollection.Enumerator _inner;

            internal Enumerator(Dictionary<long, ItemInstance<TState>>.ValueCollection.Enumerator inner)
            {
                _inner = inner;
            }

            /// <summary>Current instance.</summary>
            public ItemInstance<TState> Current => _inner.Current;

            /// <summary>Advances to the next instance.</summary>
            public bool MoveNext() => _inner.MoveNext();
        }
    }
}
