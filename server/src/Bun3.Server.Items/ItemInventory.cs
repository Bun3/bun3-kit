using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 스택/인스턴스 통합 인벤토리 — 스택형·비스택형 판정을 카탈로그 메타로 내부에서
    /// 정확히 한 번 수행한다(호출자는 몰라도 된다). 스택형 정의는 정의당 싱글턴
    /// 인스턴스로 자동 병합, 비스택형은 수량 1 인스턴스 N개. 수량은 long 고정 —
    /// BigNum 대수량 재화는 <see cref="BigNumItemStackContainer"/> 몫.
    /// 모든 변경 연산은 원자적이며 실패 시 완전 무변경. 락 없음(세션 액터 단일 스레드 계약).
    /// 조회·열거·소모 경로는 무할당, 인스턴스 생성만 본질적 할당(저빈도).
    /// </summary>
    /// <typeparam name="TState">게임이 정의하는 인스턴스 상태 타입.</typeparam>
    public sealed class ItemInventory<TState>
    {
        // ponytail: 비스택형 1회 지급 인스턴스 수 상한 — 무제한 maxStack 정의에 대량 지급 시
        // 생성 루프 폭주를 막는다. 정당한 대량 지급이 필요해지면 옵션으로 승격.
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

        private static LongQuantityOps Ops => default;

        /// <summary>인벤토리를 만든다.</summary>
        /// <param name="catalog">아이템 카탈로그.</param>
        /// <param name="instanceIdIssuer">인스턴스 id 발급자 — 세션 간 고유해야 한다
        /// (스노플레이크·하이로우·DB 시퀀스 등은 게임 선택). 검증 통과 후에만 호출된다.</param>
        /// <param name="stateFactory">인스턴스 생성 시 초기 상태 팩토리.</param>
        /// <param name="capacity">초기 용량(0이면 기본).</param>
        /// <param name="onChanged">성공한 변경 연산당 1회 + <see cref="ItemInstance{TState}.MarkChanged"/>당
        /// 1회 호출 — 게임은 Player.MarkDirty를 넘겨 저장 주기와 맞물린다.</param>
        /// <param name="removeBlockingFlags">이 마스크에 걸리는 플래그의 인스턴스는 소모
        /// 후보에서 제외된다(예: 사용 중·유저 잠금). 0이면 잠금 없음.</param>
        public ItemInventory(
            ItemCatalog catalog,
            Func<long> instanceIdIssuer,
            Func<ItemId, TState> stateFactory,
            int capacity = 0,
            Action? onChanged = null,
            uint removeBlockingFlags = 0)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _instanceIdIssuer = instanceIdIssuer ?? throw new ArgumentNullException(nameof(instanceIdIssuer));
            _stateFactory = stateFactory ?? throw new ArgumentNullException(nameof(stateFactory));
            _onChanged = onChanged;
            _removeBlockingFlags = removeBlockingFlags;
            _instances = new Dictionary<long, ItemInstance<TState>>(capacity);
            _stackSingletons = new Dictionary<ItemId, long>();
        }

        /// <summary>이 인벤토리가 묶인 카탈로그.</summary>
        public ItemCatalog Catalog => _catalog;

        /// <summary>보유 인스턴스 수(스택 싱글턴 포함).</summary>
        public int InstanceCount => _instances.Count;

        /// <summary>마지막 드레인 이후 변경이 있는지 여부.</summary>
        public bool HasChanges => _hasChanges;

        /// <summary>정의의 총 보유 수량 — 스택형은 싱글턴 수량, 비스택형은 인스턴스 수. 미보유면 0.</summary>
        public long GetQuantity(ItemId item)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                return _instances[singletonId].Quantity;
            }

            long total = 0;
            // ponytail: 정의별 색인 없이 전체 스캔(O(인스턴스 수)) — 플레이어 인벤 수백 규모 전제.
            foreach (var entry in _instances)
            {
                if (entry.Value.Item == item)
                {
                    total += entry.Value.Quantity;
                }
            }

            return total;
        }

        /// <summary>인스턴스 id로 조회한다.</summary>
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

        /// <summary>보유 인스턴스 열거 — foreach 무할당(struct 열거자).</summary>
        public Enumerator GetEnumerator() => new Enumerator(_instances.Values.GetEnumerator());

        internal void OnInstanceChanged()
        {
            _hasChanges = true;
            _onChanged?.Invoke();
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

        /// <summary>딕셔너리 값 struct 열거자를 감싼 인스턴스 열거자.</summary>
        public struct Enumerator
        {
            private Dictionary<long, ItemInstance<TState>>.ValueCollection.Enumerator _inner;

            internal Enumerator(Dictionary<long, ItemInstance<TState>>.ValueCollection.Enumerator inner)
            {
                _inner = inner;
            }

            /// <summary>현재 인스턴스.</summary>
            public ItemInstance<TState> Current => _inner.Current;

            /// <summary>다음 인스턴스로 이동한다.</summary>
            public bool MoveNext() => _inner.MoveNext();
        }
    }
}
