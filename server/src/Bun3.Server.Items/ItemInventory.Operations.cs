// ItemInventory partial — grant, consume, and transaction (atomic commit).
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    // Every mutating API goes through the single commit core (CommitOps).
    public sealed partial class ItemInventory<TState>
    {
        private static readonly BigNum MaxInstancesPerOperationQuantity = MaxInstancesPerOperation;

        internal enum TxOpKind : byte
        {
            Add,
            RemoveByItem,
            RemoveInstanceAll,
            RemoveInstanceAmount,
        }

        internal readonly struct TxOp
        {
            internal TxOp(TxOpKind kind, ItemId item, long instanceId, BigNum amount)
            {
                Kind = kind;
                Item = item;
                InstanceId = instanceId;
                Amount = amount;
            }

            internal TxOpKind Kind { get; }
            internal ItemId Item { get; }
            internal long InstanceId { get; }
            internal BigNum Amount { get; }
        }

        /// <summary>
        /// Starts a transaction builder — an atomic batch mixing definition-level and
        /// instance-targeted operations. Only one batch at a time: a new Begin discards the
        /// previous uncommitted batch, and using a stale builder throws.
        /// </summary>
        public InventoryTransaction<TState> BeginTransaction()
        {
            _txOps.Clear();
            _txToken++;
            return new InventoryTransaction<TState>(this, _txToken);
        }

        internal void TxRecord(int token, TxOp op)
        {
            if (token != _txToken)
            {
                throw new InvalidOperationException("Stale transaction builder — only the builder from the latest BeginTransaction is valid.");
            }

            _txOps.Add(op);
        }

        internal InventoryError TxCommit(int token, out int failedIndex, List<ItemInstance<TState>>? created)
        {
            if (token != _txToken)
            {
                throw new InvalidOperationException("Stale transaction builder — only the builder from the latest BeginTransaction is valid.");
            }

            _txToken++;   // Invalidate the builder after commit.
            var error = CommitOps(_txOps, out failedIndex, created);
            _txOps.Clear();
            return error;
        }

        /// <summary>Grants an amount. amount must be positive. For unstackables, amount instances
        /// (integer, capped at <see cref="MaxInstancesPerOperation"/>) are created and collected
        /// into <paramref name="created"/> (including a newly created stack singleton).</summary>
        public InventoryError TryAdd(ItemId item, BigNum amount, List<ItemInstance<TState>>? created = null)
        {
            _applyOps.Clear();
            _applyOps.Add(new TxOp(TxOpKind.Add, item, 0, amount));
            return CommitOps(_applyOps, out _, created);
        }

        /// <summary>
        /// Grants as much as possible (clamped) — hitting the cap is a partial grant, not a failure.
        /// <paramref name="granted"/> is the actual granted amount (0 included — 0 means no change
        /// and no notification); handling the remainder (mail, etc.) is the game's job.
        /// The only errors are UnknownItem and InvalidAmount (non-positive, or non-integer for
        /// unstackables). Under lossy stackable BigNum semantics, the reported grant may differ
        /// from the balance change (a tiny amount absorbed into a huge stack).
        /// </summary>
        public InventoryError TryAddUpTo(
            ItemId item,
            BigNum amount,
            out BigNum granted,
            List<ItemInstance<TState>>? created = null)
        {
            granted = BigNum.Zero;
            if (!_catalog.Contains(item))
            {
                return InventoryError.UnknownItem;
            }

            if (amount.Sign <= 0)
            {
                return InventoryError.InvalidAmount;
            }

            var clamp = amount;
            if (_catalog.IsUnstackable(item))
            {
                if (amount.Exponent < 0)
                {
                    return InventoryError.InvalidAmount;
                }

                if (clamp.CompareTo(MaxInstancesPerOperationQuantity) > 0)
                {
                    clamp = MaxInstancesPerOperationQuantity;
                }
            }

            var maxCount = _catalog.GetMaxCount(item);
            if (maxCount != long.MaxValue)
            {
                var room = (BigNum)maxCount - GetQuantity(item);
                if (room.Sign <= 0)
                {
                    return InventoryError.None;   // Full — zero-grant success.
                }

                if (clamp.CompareTo(room) > 0)
                {
                    clamp = room;
                }
            }

            _applyOps.Clear();
            _applyOps.Add(new TxOp(TxOpKind.Add, item, 0, clamp));
            var error = CommitOps(_applyOps, out _, created);
            if (error == InventoryError.None)
            {
                granted = clamp;
            }

            return error;
        }

        /// <summary>Consumes an amount. amount must be positive. For unstackables, amount unlocked
        /// instances are removed (order unspecified — for a specific instance use
        /// <see cref="TryRemoveByInstance"/>).</summary>
        public InventoryError TryRemove(ItemId item, BigNum amount)
        {
            _applyOps.Clear();
            _applyOps.Add(new TxOp(TxOpKind.RemoveByItem, item, 0, amount));
            return CommitOps(_applyOps, out _, null);
        }

        /// <summary>Available (unlocked) amount for a definition — for consumable-amount display and
        /// priority-consumption planning.</summary>
        public BigNum GetRemovableQuantity(ItemId item)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                return (singleton.Flags & _removeBlockingFlags) != 0 ? BigNum.Zero : singleton.Quantity;
            }

            long count = 0;
            foreach (var entry in _instances)
            {
                if (entry.Value.Item == item && (entry.Value.Flags & _removeBlockingFlags) == 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Sum of total held amounts across definitions — for split-pool display
        /// (regen/reward, free/paid).</summary>
        public BigNum GetQuantityAcross(ReadOnlySpan<ItemId> items)
        {
            var total = BigNum.Zero;
            for (var i = 0; i < items.Length; i++)
            {
                total += GetQuantity(items[i]);
            }

            return total;
        }

        /// <summary>
        /// Consumes across multiple definitions in priority order — all-or-nothing. The consume
        /// path of the split-pool pattern (reward tickets first, free currency first, etc. — the
        /// caller defines the order). If the available (unlocked) sum is insufficient, returns
        /// <see cref="InventoryError.Insufficient"/> with no change.
        /// Unstackable sources work exactly only for integer amounts.
        /// </summary>
        /// <param name="sources">Definitions in consumption priority order (no duplicates).</param>
        /// <param name="amount">Total amount to consume (positive).</param>
        public InventoryError TryRemoveAcross(ReadOnlySpan<ItemId> sources, BigNum amount)
        {
            if (amount.Sign <= 0)
            {
                return InventoryError.InvalidAmount;
            }

            _applyOps.Clear();
            var remaining = amount;
            for (var i = 0; i < sources.Length && remaining.Sign > 0; i++)
            {
                if (!_catalog.Contains(sources[i]))
                {
                    return InventoryError.UnknownItem;
                }

                var available = GetRemovableQuantity(sources[i]);
                if (available.Sign <= 0)
                {
                    continue;
                }

                var take = available.CompareTo(remaining) < 0 ? available : remaining;
                _applyOps.Add(new TxOp(TxOpKind.RemoveByItem, sources[i], 0, take));
                remaining -= take;
            }

            if (remaining.Sign > 0)
            {
                return InventoryError.Insufficient;
            }

            return CommitOps(_applyOps, out _, null);
        }

        /// <summary>Consumes an amount from a specific instance. Locked instances return
        /// <see cref="InventoryError.Locked"/>. Unstackables have amount 1, so amount 1 removes
        /// the instance itself.</summary>
        public InventoryError TryRemoveByInstance(long instanceId, BigNum amount)
        {
            _applyOps.Clear();
            _applyOps.Add(new TxOp(TxOpKind.RemoveInstanceAmount, ItemId.None, instanceId, amount));
            return CommitOps(_applyOps, out _, null);
        }

        /// <summary>
        /// Applies multiple deltas all-or-nothing. Stackable and unstackable may be mixed — the
        /// distinction is resolved once internally. Validation is sequential (earlier deltas for
        /// the same definition accumulate); on failure the inventory is fully unchanged and
        /// <paramref name="failedIndex"/> points at the offending delta.
        /// </summary>
        public InventoryError TryApply(
            ReadOnlySpan<ItemDelta> deltas,
            out int failedIndex,
            List<ItemInstance<TState>>? created = null)
        {
            _applyOps.Clear();
            for (var i = 0; i < deltas.Length; i++)
            {
                var delta = deltas[i];
                _applyOps.Add(delta.Amount.Sign >= 0
                    ? new TxOp(TxOpKind.Add, delta.Item, 0, delta.Amount)
                    : new TxOp(TxOpKind.RemoveByItem, delta.Item, 0, -delta.Amount));
            }

            return CommitOps(_applyOps, out failedIndex, created);
        }

        /// <summary>
        /// Commit core — every operation must pass the validation pass (amount simulation) before
        /// the apply pass runs. Unstackable instances targeted by instance-level consumes are
        /// excluded from the definition-level candidate pool for the whole batch (order-independent).
        /// The id issuer and state factory are called only after validation passes; on success
        /// onChanged fires once per batch.
        /// </summary>
        private InventoryError CommitOps(List<TxOp> ops, out int failedIndex, List<ItemInstance<TState>>? created)
        {
            failedIndex = -1;
            if (ops.Count == 0)
            {
                return InventoryError.None;
            }

            // Collect targeted instances — unstackable targets are globally excluded from
            // the definition-level consume pool.
            _txUnstackableTargets.Clear();
            for (var i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (op.Kind != TxOpKind.RemoveInstanceAll && op.Kind != TxOpKind.RemoveInstanceAmount)
                {
                    continue;
                }

                if (_instances.TryGetValue(op.InstanceId, out var target) && _catalog.IsUnstackable(target.Item))
                {
                    _txUnstackableTargets.Add(op.InstanceId);
                }
            }

            // Validation pass — simulate (total, available pool) per definition and resolve
            // instance-targeted consume amounts.
            _txConsumedTargets.Clear();
            _txResolved.Clear();
            _txNetIds.Clear();
            _txNetTotal.Clear();
            _txNetPool.Clear();
            for (var i = 0; i < ops.Count; i++)
            {
                var error = ValidateOp(ops[i], out var resolved);
                _txResolved.Add(resolved);
                if (error != InventoryError.None)
                {
                    failedIndex = i;
                    return error;
                }
            }

            // Apply pass — applied in order, in a state where failure is impossible.
            _appliedCount = 0;
            for (var i = 0; i < ops.Count; i++)
            {
                ApplyOp(ops[i], _txResolved[i], created);
            }

            _hasChanges = true;
            _onChanged?.Invoke();
            if (_appliedCount > 0)
            {
                _onApplied?.Invoke(new ReadOnlySpan<InventoryChange>(_applied, 0, _appliedCount));
                LogAppliedChanges();
            }

            return InventoryError.None;
        }

        private InventoryError ValidateOp(TxOp op, out BigNum resolved)
        {
            resolved = BigNum.Zero;
            switch (op.Kind)
            {
                case TxOpKind.Add:
                case TxOpKind.RemoveByItem:
                {
                    if (!_catalog.Contains(op.Item))
                    {
                        return InventoryError.UnknownItem;
                    }

                    if (op.Amount.Sign <= 0)
                    {
                        return InventoryError.InvalidAmount;
                    }

                    if (_catalog.IsUnstackable(op.Item))
                    {
                        if (!TryToInstanceCount(op.Amount, out _))
                        {
                            return InventoryError.InvalidAmount;
                        }
                    }
                    else if (_catalog.GetRegenPeriodTicks(op.Item) > 0 && op.Amount.Exponent < 0)
                    {
                        return InventoryError.InvalidAmount;   // Regen definitions allow integer amounts only — settlement arithmetic depends on it.
                    }

                    var net = NetIndexOf(op.Item);
                    if (op.Kind == TxOpKind.Add)
                    {
                        if (!TryAddQuantity(_txNetTotal[net], op.Amount, out var total))
                        {
                            return InventoryError.ExceedsMaxCount;
                        }

                        var maxCount = _catalog.GetMaxCount(op.Item);
                        if (maxCount != long.MaxValue && total.CompareTo(maxCount) > 0)
                        {
                            return InventoryError.ExceedsMaxCount;
                        }

                        _txNetTotal[net] = total;
                        _txNetPool[net] += op.Amount;   // Newly granted amounts carry no locks — available.
                        return InventoryError.None;
                    }

                    if (_txNetPool[net].CompareTo(op.Amount) < 0)
                    {
                        return InventoryError.Insufficient;
                    }

                    _txNetPool[net] -= op.Amount;
                    _txNetTotal[net] -= op.Amount;
                    return InventoryError.None;
                }

                default:
                {
                    if (!_instances.TryGetValue(op.InstanceId, out var target))
                    {
                        return InventoryError.UnknownInstance;
                    }

                    if (op.Kind == TxOpKind.RemoveInstanceAmount && op.Amount.Sign <= 0)
                    {
                        return InventoryError.InvalidAmount;
                    }

                    if ((target.Flags & _removeBlockingFlags) != 0)
                    {
                        return InventoryError.Locked;
                    }

                    if (_catalog.IsUnstackable(target.Item))
                    {
                        if (op.Kind == TxOpKind.RemoveInstanceAmount)
                        {
                            var comparison = op.Amount.CompareTo(BigNum.One);
                            if (comparison > 0)
                            {
                                return InventoryError.Insufficient;
                            }

                            if (comparison < 0)
                            {
                                return InventoryError.InvalidAmount;
                            }
                        }

                        for (var i = 0; i < _txConsumedTargets.Count; i++)
                        {
                            if (_txConsumedTargets[i] == op.InstanceId)
                            {
                                return InventoryError.Insufficient;   // Same instance targeted twice.
                            }
                        }

                        _txConsumedTargets.Add(op.InstanceId);
                        var netUnstackable = NetIndexOf(target.Item);
                        _txNetTotal[netUnstackable] -= BigNum.One;   // Pool already excluded globally.
                        resolved = BigNum.One;
                        return InventoryError.None;
                    }

                    // Targeted stack singleton — settles against the same pool as definition-level consumes.
                    var netSingleton = NetIndexOf(target.Item);
                    var amount = op.Kind == TxOpKind.RemoveInstanceAll ? _txNetPool[netSingleton] : op.Amount;
                    if (amount.Sign <= 0 || _txNetPool[netSingleton].CompareTo(amount) < 0)
                    {
                        return InventoryError.Insufficient;
                    }

                    _txNetPool[netSingleton] -= amount;
                    _txNetTotal[netSingleton] -= amount;
                    resolved = amount;
                    return InventoryError.None;
                }
            }
        }

        private void ApplyOp(TxOp op, BigNum resolved, List<ItemInstance<TState>>? created)
        {
            switch (op.Kind)
            {
                case TxOpKind.Add:
                    ApplyGrant(op.Item, op.Amount, created);
                    RecordApplied(op.Item, op.Amount);
                    return;

                case TxOpKind.RemoveByItem:
                    ApplyConsume(op.Item, op.Amount);
                    RecordApplied(op.Item, -op.Amount);
                    return;

                default:
                {
                    var target = _instances[op.InstanceId];
                    var item = target.Item;
                    if (target.Quantity.CompareTo(resolved) == 0)
                    {
                        RemoveInstance(target);
                    }
                    else
                    {
                        target.Quantity -= resolved;
                        MarkChangedNoNotify(target);
                    }

                    RecordApplied(item, -resolved);
                    return;
                }
            }
        }

        /// <summary>Attaches the applied-buffer changes to the action log — low-frequency audit path, boxing allowed.</summary>
        private void LogAppliedChanges()
        {
            if (_log == null)
            {
                return;
            }

            for (var i = 0; i < _appliedCount; i++)
            {
                _log.Append(_applied[i], _logLabel);
            }
        }

        private void RecordApplied(ItemId item, BigNum delta)
        {
            if (_onApplied == null && _log == null)
            {
                return;
            }

            if (_appliedCount == _applied.Length)
            {
                System.Array.Resize(ref _applied, _applied.Length == 0 ? 8 : _applied.Length * 2);
            }

            // Balance is read right after applying — becomes the ledger's post-change balance column.
            _applied[_appliedCount++] = new InventoryChange(item, delta, GetQuantity(item));
        }

        private void ApplyGrant(ItemId item, BigNum amount, List<ItemInstance<TState>>? created)
        {
            if (_catalog.IsUnstackable(item))
            {
                TryToInstanceCount(amount, out var count);   // Passed validation — cannot fail.
                for (var i = 0; i < count; i++)
                {
                    var instance = CreateInstance(item, BigNum.One);
                    created?.Add(instance);
                }

                return;
            }

            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                singleton.Quantity += amount;
                MarkChangedNoNotify(singleton);
            }
            else
            {
                var instance = CreateInstance(item, amount);
                _stackSingletons.Add(item, instance.InstanceId);
                created?.Add(instance);
            }
        }

        private void ApplyConsume(ItemId item, BigNum amount)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                if (singleton.Quantity.CompareTo(amount) == 0)
                {
                    RemoveInstance(singleton);
                }
                else
                {
                    singleton.Quantity -= amount;
                    MarkChangedNoNotify(singleton);
                }

                return;
            }

            // Unstackable — collect count instances that are neither locked nor batch-targeted, then remove.
            TryToInstanceCount(amount, out var count);   // Passed validation — cannot fail.
            _removeScratch.Clear();
            foreach (var entry in _instances)
            {
                if (_removeScratch.Count >= count)
                {
                    break;
                }

                if (entry.Value.Item == item
                    && (entry.Value.Flags & _removeBlockingFlags) == 0
                    && !IsUnstackableTarget(entry.Key))
                {
                    _removeScratch.Add(entry.Value);
                }
            }

            for (var i = 0; i < _removeScratch.Count; i++)
            {
                RemoveInstance(_removeScratch[i]);
            }

            _removeScratch.Clear();
        }

        /// <summary>Finds or creates the per-definition simulation entry — (total, available pool)
        /// starts from the current holdings and availability excluding locked and targeted instances.</summary>
        private int NetIndexOf(ItemId item)
        {
            for (var i = 0; i < _txNetIds.Count; i++)
            {
                if (_txNetIds[i] == item)
                {
                    return i;
                }
            }

            _txNetIds.Add(item);
            _txNetTotal.Add(GetQuantity(item));
            _txNetPool.Add(GetRemovablePoolBase(item));
            return _txNetIds.Count - 1;
        }

        private BigNum GetRemovablePoolBase(ItemId item)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                return (singleton.Flags & _removeBlockingFlags) != 0 ? BigNum.Zero : singleton.Quantity;
            }

            long count = 0;
            foreach (var entry in _instances)
            {
                if (entry.Value.Item == item
                    && (entry.Value.Flags & _removeBlockingFlags) == 0
                    && !IsUnstackableTarget(entry.Key))
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsUnstackableTarget(long instanceId)
        {
            for (var i = 0; i < _txUnstackableTargets.Count; i++)
            {
                if (_txUnstackableTargets[i] == instanceId)
                {
                    return true;
                }
            }

            return false;
        }

        private ItemInstance<TState> CreateInstance(ItemId item, BigNum quantity)
        {
            var instance = new ItemInstance<TState>(
                this, _instanceIdIssuer(), item, quantity, 0, 0, _stateFactory(item))
            {
                IsNew = true,
            };
            _instances.Add(instance.InstanceId, instance);
            return instance;
        }

        private void RemoveInstance(ItemInstance<TState> instance)
        {
            _instances.Remove(instance.InstanceId);
            if (_stackSingletons.TryGetValue(instance.Item, out var singletonId) && singletonId == instance.InstanceId)
            {
                _stackSingletons.Remove(instance.Item);
            }

            if (!instance.IsNew)
            {
                _removed.Add(instance.InstanceId);
            }

            instance._owner = null;
        }

        private static void MarkChangedNoNotify(ItemInstance<TState> instance) => instance.Changed = true;

        /// <summary>BigNum addition — exceeding the exponent limit yields false (for all-or-nothing validation).</summary>
        private static bool TryAddQuantity(BigNum a, BigNum b, out BigNum result)
        {
            try
            {
                result = a + b;
                return true;
            }
            catch (BigNumOverflowException)
            {
                result = BigNum.Zero;
                return false;
            }
        }

        /// <summary>Unstackable amount conversion — only positive integers up to
        /// <see cref="MaxInstancesPerOperation"/> are allowed.</summary>
        private static bool TryToInstanceCount(BigNum amount, out int count)
        {
            count = 0;
            if (amount.Exponent < 0 || amount.CompareTo(MaxInstancesPerOperationQuantity) > 0)
            {
                return false;
            }

            long value = amount.Mantissa;
            for (var i = 0; i < amount.Exponent; i++)
            {
                value *= 10;
            }

            count = (int)value;
            return true;
        }
    }
}
