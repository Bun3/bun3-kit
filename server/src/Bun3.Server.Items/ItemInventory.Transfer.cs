using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    // 인벤토리 간 이동 — 같은 플레이어의 컨테이너 간(가방↔창고, 캐릭터별) 원자 이동.
    // 유저간 거래가 아니다: 두 인벤토리는 같은 세션 액터(단일 스레드)에서 조작되는 것이
    // 계약이며, 다른 플레이어와의 교환은 에스크로/우편 워크플로 몫(별도 모듈 후보).
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>
        /// 정의 단위 이동 — 스택형은 수량이 대상 싱글턴에 병합되고(인스턴스 id는 수량
        /// 의미론상 보존하지 않음), 비스택형은 잠금 아닌 인스턴스 amount개가 id·상태·
        /// 플래그·만료를 보존한 채 통째로 옮겨진다. 실패 시 양쪽 모두 무변경.
        /// 대상은 같은 카탈로그여야 하며(다르면 구성 오류로 던짐) 대상 maxCount를 검사한다.
        /// 성공 시 양쪽에 onChanged·onApplied(출발 −, 도착 +)가 각 1회.
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
                            error = InventoryError.DuplicateInstance;   // 발급자 교차 구성 오류 방어
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
        /// 인스턴스 전량 이동 — 비스택형은 인스턴스가 id·상태를 보존한 채 옮겨지고,
        /// 스택 싱글턴은 잔량 전부가 정의 단위로 이동한다. 잠금 인스턴스는
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
                throw new ArgumentException("자기 자신으로는 이동할 수 없습니다.", nameof(target));
            }

            if (!ReferenceEquals(target._catalog, _catalog))
            {
                throw new ArgumentException("이동은 같은 카탈로그의 인벤토리 사이에서만 가능합니다.", nameof(target));
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

        /// <summary>인스턴스를 id·상태·플래그·만료 보존한 채 대상으로 옮긴다 — 출발은
        /// Removed, 도착은 Created로 추적된다(게임 DB에선 소유 컬럼 UPDATE로 매핑 가능).</summary>
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
            _log?.AppendChanges(_logLabel, new ReadOnlySpan<InventoryChange>(_applied, 0, _appliedCount));
        }
    }
}
