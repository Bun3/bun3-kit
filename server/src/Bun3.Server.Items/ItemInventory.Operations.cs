using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    // 지급·소모·트랜잭션 — 스택/인스턴스 판정과 원자적 적용의 본체.
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>수량을 지급한다. amount는 양수여야 한다. 비스택형은 amount개 인스턴스가
        /// 생성되며 <paramref name="created"/>에 담긴다(스택 싱글턴 신규 생성 포함).</summary>
        public ItemError TryAdd(ItemId item, long amount, List<ItemInstance<TState>>? created = null)
        {
            if (amount <= 0)
            {
                return ItemError.InvalidAmount;
            }

            Span<ItemDelta<long>> delta = stackalloc ItemDelta<long>[1];
            delta[0] = new ItemDelta<long>(item, amount);
            return TryApply(delta, out _, created);
        }

        /// <summary>수량을 소모한다. amount는 양수여야 한다. 비스택형은 잠금 아닌 인스턴스
        /// amount개가 제거된다(순서 미보장 — 특정 인스턴스는 <see cref="TryRemoveByInstance"/>).</summary>
        public ItemError TryRemove(ItemId item, long amount)
        {
            if (amount <= 0)
            {
                return ItemError.InvalidAmount;
            }

            Span<ItemDelta<long>> delta = stackalloc ItemDelta<long>[1];
            delta[0] = new ItemDelta<long>(item, -amount);
            return TryApply(delta, out _, null);
        }

        /// <summary>특정 인스턴스에서 수량을 소모한다. 잠금 인스턴스는 <see cref="ItemError.Locked"/>.
        /// 비스택형은 수량이 1이므로 amount 1로 인스턴스 자체가 제거된다.</summary>
        public ItemError TryRemoveByInstance(long instanceId, long amount)
        {
            if (!_instances.TryGetValue(instanceId, out var instance))
            {
                return ItemError.UnknownInstance;
            }

            if (amount <= 0)
            {
                return ItemError.InvalidAmount;
            }

            if ((instance.Flags & _removeBlockingFlags) != 0)
            {
                return ItemError.Locked;
            }

            if (amount > instance.Quantity)
            {
                return ItemError.Insufficient;
            }

            if (amount == instance.Quantity)
            {
                RemoveInstance(instance);
            }
            else
            {
                instance.Quantity -= amount;
                MarkChangedNoNotify(instance);
            }

            _hasChanges = true;
            _onChanged?.Invoke();
            return ItemError.None;
        }

        /// <summary>
        /// 복수 델타를 전부-아니면-전무로 적용한다. 스택형·비스택형 혼합 가능 — 판정은
        /// 내부에서 1회. 순차 판정(같은 정의의 앞선 델타 누적 반영)이며 실패 시 완전
        /// 무변경, <paramref name="failedIndex"/>가 원인 델타를 가리킨다. id 발급자·상태
        /// 팩토리는 검증 통과 후에만 호출된다. 성공 시 onChanged는 배치당 1회.
        /// </summary>
        public ItemError TryApply(
            ReadOnlySpan<ItemDelta<long>> deltas,
            out int failedIndex,
            List<ItemInstance<TState>>? created = null)
        {
            failedIndex = -1;
            if (deltas.Length == 0)
            {
                return ItemError.None;
            }

            for (var i = 0; i < deltas.Length; i++)
            {
                var delta = deltas[i];
                if (!_catalog.Contains(delta.Item))
                {
                    failedIndex = i;
                    return ItemError.UnknownItem;
                }

                if (delta.Amount == 0)
                {
                    failedIndex = i;
                    return ItemError.InvalidAmount;
                }

                // ponytail: 같은 정의의 앞선 델타를 재스캔(O(n²)) — 배치가 커지면 스크래치 맵 도입.
                long priorNet = 0;
                for (var j = 0; j < i; j++)
                {
                    if (deltas[j].Item == delta.Item)
                    {
                        priorNet += deltas[j].Amount;
                    }
                }

                var error = ValidateDelta(delta.Item, priorNet, delta.Amount);
                if (error != ItemError.None)
                {
                    failedIndex = i;
                    return error;
                }
            }

            for (var i = 0; i < deltas.Length; i++)
            {
                var delta = deltas[i];
                if (delta.Amount > 0)
                {
                    ApplyGrant(delta.Item, delta.Amount, created);
                }
                else
                {
                    ApplyConsume(delta.Item, -delta.Amount);
                }
            }

            _hasChanges = true;
            _onChanged?.Invoke();
            return ItemError.None;
        }

        /// <summary>델타 1건을 순차 시뮬레이션 상태(priorNet)에서 판정한다.</summary>
        private ItemError ValidateDelta(ItemId item, long priorNet, long amount)
        {
            if (amount > 0)
            {
                if (_catalog.IsUnstackable(item) && amount > MaxInstancesPerOperation)
                {
                    return ItemError.InvalidAmount;
                }

                var total = GetQuantity(item) + priorNet;
                if (!Ops.TryAdd(total, amount, out var result))
                {
                    return ItemError.ExceedsMaxStack;
                }

                var maxStack = _catalog.GetMaxStack(item);
                if (maxStack != long.MaxValue && result > maxStack)
                {
                    return ItemError.ExceedsMaxStack;
                }

                return ItemError.None;
            }

            // 소모 — 잠금 인스턴스를 제외한 가용 수량 기준. 앞선 지급분은 잠금 없이 생성되므로 가용.
            var removable = GetRemovableQuantity(item) + priorNet;
            return removable + amount < 0 ? ItemError.Insufficient : ItemError.None;
        }

        private long GetRemovableQuantity(ItemId item)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                return (singleton.Flags & _removeBlockingFlags) != 0 ? 0 : singleton.Quantity;
            }

            long total = 0;
            foreach (var entry in _instances)
            {
                if (entry.Value.Item == item && (entry.Value.Flags & _removeBlockingFlags) == 0)
                {
                    total += entry.Value.Quantity;
                }
            }

            return total;
        }

        private void ApplyGrant(ItemId item, long amount, List<ItemInstance<TState>>? created)
        {
            if (_catalog.IsUnstackable(item))
            {
                for (long i = 0; i < amount; i++)
                {
                    var instance = CreateInstance(item, 1);
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

        private void ApplyConsume(ItemId item, long amount)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                if (singleton.Quantity == amount)
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

            // 비스택형 — 잠금 아닌 인스턴스를 amount개 수집 후 제거(열거 중 변경 회피).
            _removeScratch.Clear();
            foreach (var entry in _instances)
            {
                if (_removeScratch.Count >= (int)amount)
                {
                    break;
                }

                if (entry.Value.Item == item && (entry.Value.Flags & _removeBlockingFlags) == 0)
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

        private ItemInstance<TState> CreateInstance(ItemId item, long quantity)
        {
            var instance = new ItemInstance<TState>(
                this, _instanceIdIssuer(), item, quantity, 0, _stateFactory(item))
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
    }
}
