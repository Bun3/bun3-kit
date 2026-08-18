using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    // 변경 추적과 저장 로드 — DB upsert/delete와 클라 전송의 원천.
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>마지막 드레인 이후 변경이 있는지 여부.</summary>
        public bool HasChanges => _hasChanges;

        /// <summary>
        /// 저장 로드용 — 기존 인스턴스 id(DB·Steam 권위)를 그대로 수용하고 추적·통지하지
        /// 않는다. 중복 id·스택형 정의의 두 번째 인스턴스는 <see cref="InventoryError.DuplicateInstance"/>,
        /// 비스택형에 수량 1 외는 <see cref="InventoryError.InvalidAmount"/>, maxCount 검사 수행.
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
                    return InventoryError.InvalidAmount;   // 리젠 정의는 정수 수량만
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
        /// 마지막 드레인 이후의 변경(Created/Updated/Removed)을 버퍼에 담고 추적을
        /// 초기화한다. 순서는 Removed 먼저, 이후 순서 미보장. 드레인은 인메모리 스냅샷이며
        /// 파괴적이다 — <b>버퍼가 변경 집합의 소유권을 넘겨받으므로</b> 게임은 영속화 성공
        /// 전까지 버퍼를 유지하고, 실패 시 같은 버퍼로 재시도한다(Created/Updated는 인스턴스
        /// 참조라 재시도 시점의 최신 상태가 나간다). DB I/O는 게임의 async 저장 훅 몫.
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
