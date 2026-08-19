// ItemInventory partial — change tracking (DrainChanges) and instance loading.
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    // Change tracking and save loading — the source for DB upsert/delete and client sync.
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>Whether there are changes since the last drain.</summary>
        public bool HasChanges => _hasChanges;

        /// <summary>
        /// For save loading — accepts an existing instance id (DB/Steam authoritative) as-is,
        /// with no tracking or notification. Duplicate ids and a second instance of a stackable
        /// definition return <see cref="InventoryError.DuplicateInstance"/>; unstackables with
        /// amount other than 1 return <see cref="InventoryError.InvalidAmount"/>; maxCount is checked.
        /// </summary>
        public InventoryError TryLoadInstance(
            long instanceId,
            ItemId item,
            BigNum quantity,
            uint flags,
            TState state,
            long expiresAtTicksUtc = 0)
        {
            if (!_catalog.Contains(item))
            {
                return InventoryError.UnknownItem;
            }

            if (quantity.Sign <= 0)
            {
                return InventoryError.InvalidAmount;
            }

            if (_instances.ContainsKey(instanceId))
            {
                return InventoryError.DuplicateInstance;
            }

            var maxCount = _catalog.GetMaxCount(item);
            if (_catalog.IsUnstackable(item))
            {
                if (quantity.CompareTo(BigNum.One) != 0)
                {
                    return InventoryError.InvalidAmount;
                }

                if (maxCount != long.MaxValue && GetQuantity(item).CompareTo(maxCount - 1) > 0)
                {
                    return InventoryError.ExceedsMaxCount;
                }
            }
            else
            {
                if (_stackSingletons.ContainsKey(item))
                {
                    return InventoryError.DuplicateInstance;
                }

                if (_catalog.GetRegenPeriodTicks(item) > 0 && quantity.Exponent < 0)
                {
                    return InventoryError.InvalidAmount;   // Regen definitions allow integer amounts only.
                }

                if (maxCount != long.MaxValue && quantity.CompareTo(maxCount) > 0)
                {
                    return InventoryError.ExceedsMaxCount;
                }

                _stackSingletons.Add(item, instanceId);
            }

            _instances.Add(instanceId, new ItemInstance<TState>(this, instanceId, item, quantity, flags, expiresAtTicksUtc, state));
            return InventoryError.None;
        }

        /// <summary>
        /// Collects the changes since the last drain (Created/Updated/Removed) into the buffer and
        /// resets tracking. Removed entries come first; order is otherwise unspecified. The drain is
        /// an in-memory snapshot and destructive — <b>the buffer takes ownership of the change set</b>,
        /// so the game keeps the buffer until persistence succeeds and retries with the same buffer
        /// on failure (Created/Updated hold instance references, so retries carry the latest state).
        /// DB I/O belongs to the game's async save hook.
        /// </summary>
        public void DrainChanges(List<ItemChange<TState>> buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            for (var i = 0; i < _removed.Count; i++)
            {
                buffer.Add(new ItemChange<TState>(ItemChangeKind.Removed, _removed[i], null));
            }

            _removed.Clear();

            foreach (var entry in _instances)
            {
                var instance = entry.Value;
                if (instance.IsNew)
                {
                    buffer.Add(new ItemChange<TState>(ItemChangeKind.Created, instance.InstanceId, instance));
                }
                else if (instance.Changed)
                {
                    buffer.Add(new ItemChange<TState>(ItemChangeKind.Updated, instance.InstanceId, instance));
                }

                instance.IsNew = false;
                instance.Changed = false;
            }

            _hasChanges = false;
        }

        internal void OnInstanceChanged()
        {
            _hasChanges = true;
            _onChanged?.Invoke();
        }
    }
}
