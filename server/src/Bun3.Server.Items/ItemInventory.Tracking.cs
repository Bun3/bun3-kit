using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    // 변경 추적과 저장 로드 — DB upsert/delete와 클라 전송의 원천.
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>마지막 드레인 이후 변경이 있는지 여부.</summary>
        public bool HasChanges => _hasChanges;

        /// <summary>
        /// 저장 로드용 — 기존 인스턴스 id(DB·Steam 권위)를 그대로 수용하고 추적·통지하지
        /// 않는다. 중복 id·스택형 정의의 두 번째 인스턴스는 <see cref="ItemError.DuplicateInstance"/>,
        /// 비스택형에 수량 1 외는 <see cref="ItemError.InvalidAmount"/>, maxStack 검사 수행.
        /// </summary>
        public ItemError TryLoadInstance(long instanceId, ItemId item, long quantity, uint flags, TState state)
        {
            if (!_catalog.Contains(item))
            {
                return ItemError.UnknownItem;
            }

            if (quantity <= 0)
            {
                return ItemError.InvalidAmount;
            }

            if (_instances.ContainsKey(instanceId))
            {
                return ItemError.DuplicateInstance;
            }

            var maxStack = _catalog.GetMaxStack(item);
            if (_catalog.IsUnstackable(item))
            {
                if (quantity != 1)
                {
                    return ItemError.InvalidAmount;
                }

                if (maxStack != long.MaxValue && GetQuantity(item) + 1 > maxStack)
                {
                    return ItemError.ExceedsMaxStack;
                }
            }
            else
            {
                if (_stackSingletons.ContainsKey(item))
                {
                    return ItemError.DuplicateInstance;
                }

                if (maxStack != long.MaxValue && quantity > maxStack)
                {
                    return ItemError.ExceedsMaxStack;
                }

                _stackSingletons.Add(item, instanceId);
            }

            _instances.Add(instanceId, new ItemInstance<TState>(this, instanceId, item, quantity, flags, state));
            return ItemError.None;
        }

        /// <summary>마지막 드레인 이후의 변경(Created/Updated/Removed)을 버퍼에 담고 추적을
        /// 초기화한다. 버퍼는 호출자 소유(재사용 시 무할당) — 비우지 않고 이어 담는다.
        /// 순서는 Removed 먼저, 이후 순서 미보장.</summary>
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
