// ItemInventory partial — inventory-to-inventory transfer.
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    // Transfer between containers of the same player (bag↔warehouse, per-character), atomic.
    // Not user-to-user trading: the contract is that both inventories are operated on the same
    // session actor (single thread); exchanges with other players belong to an escrow/mail
    // workflow (candidate for a separate module).
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>
        /// Definition-level transfer — stackable amounts merge into the target singleton (the
        /// instance id is not preserved under amount semantics); for unstackables, amount unlocked
        /// instances move whole, preserving id, state, flags, and expiry. On failure both sides are
        /// unchanged. The target must share the catalog (a mismatch is a configuration error and
        /// throws) and the target's maxCount is checked.
        /// On success, onChanged and onApplied (source −, target +) fire once on each side.
        /// </summary>
        public InventoryError TryTransfer(ItemInventory<TState> target, ItemId item, BigNum amount)
        {
            var error = ValidateTransferTarget(target);
            if (error != InventoryError.None)
            {
                return error;
            }

            if (!_catalog.Contains(item))
            {
                return InventoryError.UnknownItem;
            }

            if (amount.Sign <= 0)
            {
                return InventoryError.InvalidAmount;
            }

            if (_catalog.IsUnstackable(item))
            {
                if (!TryToInstanceCount(amount, out var count))
                {
                    return InventoryError.InvalidAmount;
                }

                _removeScratch.Clear();
                foreach (var entry in _instances)
                {
                    if (_removeScratch.Count >= count)
                    {
                        break;
                    }

                    if (entry.Value.Item == item && (entry.Value.Flags & _removeBlockingFlags) == 0)
                    {
                        _removeScratch.Add(entry.Value);
                    }
                }

                if (_removeScratch.Count < count)
                {
                    _removeScratch.Clear();
                    return InventoryError.Insufficient;
                }

                error = ValidateTransferCapacity(target, item, count);
                if (error == InventoryError.None)
                {
                    for (var i = 0; i < _removeScratch.Count; i++)
                    {
                        if (target._instances.ContainsKey(_removeScratch[i].InstanceId))
                        {
                            error = InventoryError.DuplicateInstance;   // Guard against crossed id-issuer configuration.
                            break;
                        }
                    }
                }

                if (error != InventoryError.None)
                {
                    _removeScratch.Clear();
                    return error;
                }

                for (var i = 0; i < _removeScratch.Count; i++)
                {
                    MoveInstance(target, _removeScratch[i]);
                }

                _removeScratch.Clear();
            }
            else
            {
                if (GetRemovableQuantity(item).CompareTo(amount) < 0)
                {
                    return InventoryError.Insufficient;
                }

                error = ValidateTransferCapacity(target, item, amount);
                if (error != InventoryError.None)
                {
                    return error;
                }

                ApplyConsume(item, amount);
                target.ApplyGrant(item, amount, null);
            }

            FinalizeTransfer(target, item, amount);
            return InventoryError.None;
        }

        /// <summary>
        /// Full-instance transfer — unstackable instances move preserving id and state; a stack
        /// singleton moves its full amount at definition level. Locked instances return
        /// <see cref="InventoryError.Locked"/>.
        /// </summary>
        public InventoryError TryTransferByInstance(ItemInventory<TState> target, long instanceId)
        {
            var error = ValidateTransferTarget(target);
            if (error != InventoryError.None)
            {
                return error;
            }

            if (!_instances.TryGetValue(instanceId, out var instance))
            {
                return InventoryError.UnknownInstance;
            }

            if ((instance.Flags & _removeBlockingFlags) != 0)
            {
                return InventoryError.Locked;
            }

            var item = instance.Item;
            var amount = instance.Quantity;
            if (_catalog.IsUnstackable(item))
            {
                error = ValidateTransferCapacity(target, item, BigNum.One);
                if (error != InventoryError.None)
                {
                    return error;
                }

                if (target._instances.ContainsKey(instanceId))
                {
                    return InventoryError.DuplicateInstance;
                }

                MoveInstance(target, instance);
            }
            else
            {
                error = ValidateTransferCapacity(target, item, amount);
                if (error != InventoryError.None)
                {
                    return error;
                }

                ApplyConsume(item, amount);
                target.ApplyGrant(item, amount, null);
            }

            FinalizeTransfer(target, item, amount);
            return InventoryError.None;
        }

        private InventoryError ValidateTransferTarget(ItemInventory<TState> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (ReferenceEquals(target, this))
            {
                throw new ArgumentException("Cannot transfer to self.", nameof(target));
            }

            if (!ReferenceEquals(target._catalog, _catalog))
            {
                throw new ArgumentException("Transfer is only allowed between inventories of the same catalog.", nameof(target));
            }

            return InventoryError.None;
        }

        private InventoryError ValidateTransferCapacity(ItemInventory<TState> target, ItemId item, BigNum amount)
        {
            var maxCount = _catalog.GetMaxCount(item);
            if (maxCount == long.MaxValue)
            {
                return InventoryError.None;
            }

            if (!TryAddQuantity(target.GetQuantity(item), amount, out var total)
                || total.CompareTo(maxCount) > 0)
            {
                return InventoryError.ExceedsMaxCount;
            }

            return InventoryError.None;
        }

        /// <summary>Moves an instance to the target preserving id, state, flags, and expiry — the
        /// source tracks Removed and the target tracks Created (a game DB may map this to an
        /// owner-column UPDATE).</summary>
        private void MoveInstance(ItemInventory<TState> target, ItemInstance<TState> instance)
        {
            _instances.Remove(instance.InstanceId);
            if (!instance.IsNew)
            {
                _removed.Add(instance.InstanceId);
            }

            instance._owner = target;
            instance.IsNew = true;
            instance.Changed = false;
            target._instances.Add(instance.InstanceId, instance);
        }

        private void FinalizeTransfer(ItemInventory<TState> target, ItemId item, BigNum amount)
        {
            _hasChanges = true;
            target._hasChanges = true;
            _onChanged?.Invoke();
            target._onChanged?.Invoke();
            EmitTransferSide(item, -amount);
            target.EmitTransferSide(item, amount);
        }

        private void EmitTransferSide(ItemId item, BigNum delta)
        {
            if (_onApplied == null && _log == null)
            {
                return;
            }

            _appliedCount = 0;
            RecordApplied(item, delta);
            _onApplied?.Invoke(new ReadOnlySpan<InventoryChange>(_applied, 0, _appliedCount));
            LogAppliedChanges();
        }
    }
}
