using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 무슬롯 스택 인벤토리 — <see cref="ItemId"/>별 수량 맵. 슬롯 배치·인스턴스
    /// 아이템은 게임 몫이다. 모든 변경 연산은 원자적이며 실패 시 컨테이너는 무변경이다.
    /// 락이 없다 — Player 상태처럼 세션 액터 안 단일 스레드 접근이 계약이다.
    /// 핫패스(TryAdd/TryRemove/TryApply/TryMoveTo/GetQuantity/열거)는 무할당이다
    /// (할당 지점은 생성 시와 딕셔너리 성장뿐).
    /// </summary>
    /// <typeparam name="TQuantity">수량 타입.</typeparam>
    /// <typeparam name="TOps">무상태 값타입 산술 구현.</typeparam>
    public class ItemStackContainer<TQuantity, TOps>
        where TOps : struct, IQuantityOps<TQuantity>
    {
        private readonly ItemCatalog _catalog;
        private readonly Dictionary<ItemId, TQuantity> _stacks;
        private readonly Action? _onChanged;

        private static TOps Ops => default;

        /// <summary>컨테이너를 만든다.</summary>
        /// <param name="catalog">아이템 카탈로그 — 모든 연산이 이 카탈로그의 식별자만 받는다.</param>
        /// <param name="capacity">초기 딕셔너리 용량(0이면 기본).</param>
        /// <param name="onChanged">성공한 변경 연산당 1회 호출 — 게임은 Player.MarkDirty를
        /// 메서드 그룹으로 넘겨 저장 주기와 맞물린다. 실패·로드 시엔 호출되지 않는다.</param>
        public ItemStackContainer(ItemCatalog catalog, int capacity = 0, Action? onChanged = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _stacks = new Dictionary<ItemId, TQuantity>(capacity);
            _onChanged = onChanged;
        }

        /// <summary>이 컨테이너가 묶인 카탈로그.</summary>
        public ItemCatalog Catalog => _catalog;

        /// <summary>보유 중인 스택 종류 수.</summary>
        public int Count => _stacks.Count;

        /// <summary>보유 수량. 미보유면 Zero.</summary>
        public TQuantity GetQuantity(ItemId item) =>
            _stacks.TryGetValue(item, out var quantity) ? quantity : Ops.Zero;

        /// <summary>보유 여부.</summary>
        public bool Contains(ItemId item) => _stacks.ContainsKey(item);

        /// <summary>수량을 추가한다. amount는 양수여야 한다.</summary>
        public ItemError TryAdd(ItemId item, TQuantity amount)
        {
            if (!_catalog.Contains(item))
            {
                return ItemError.UnknownItem;
            }

            if (Ops.Compare(amount, Ops.Zero) <= 0)
            {
                return ItemError.InvalidAmount;
            }

            var error = ValidateDelta(item, GetQuantity(item), amount, out var result);
            if (error != ItemError.None)
            {
                return error;
            }

            SetOrRemove(item, result);
            _onChanged?.Invoke();
            return ItemError.None;
        }

        /// <summary>수량을 소모한다. amount는 양수여야 한다.</summary>
        public ItemError TryRemove(ItemId item, TQuantity amount)
        {
            if (!_catalog.Contains(item))
            {
                return ItemError.UnknownItem;
            }

            if (Ops.Compare(amount, Ops.Zero) <= 0)
            {
                return ItemError.InvalidAmount;
            }

            var error = ValidateDelta(item, GetQuantity(item), Ops.Negate(amount), out var result);
            if (error != ItemError.None)
            {
                return error;
            }

            SetOrRemove(item, result);
            _onChanged?.Invoke();
            return ItemError.None;
        }

        /// <summary>
        /// 복수 델타를 전부-아니면-전무로 적용한다. 델타는 배열 순서대로 순차 판정되며
        /// (같은 아이템의 앞선 델타 누적 반영), 하나라도 실패하면 컨테이너는 완전
        /// 무변경이고 <paramref name="failedIndex"/>가 원인 델타를 가리킨다.
        /// 성공 시 onChanged는 배치당 1회다. 빈 배치는 성공이며 통지하지 않는다.
        /// </summary>
        public ItemError TryApply(ReadOnlySpan<ItemDelta<TQuantity>> deltas, out int failedIndex)
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

                if (Ops.Compare(delta.Amount, Ops.Zero) == 0)
                {
                    failedIndex = i;
                    return ItemError.InvalidAmount;
                }

                var net = GetQuantity(delta.Item);
                // ponytail: 같은 아이템의 앞선 델타를 재스캔(O(n²)) — 배치가 커지면 스크래치 맵 도입.
                // 앞선 항은 각자의 단계에서 이미 검증됐으므로 재합산은 실패할 수 없다.
                for (var j = 0; j < i; j++)
                {
                    if (deltas[j].Item.Equals(delta.Item))
                    {
                        Ops.TryAdd(net, deltas[j].Amount, out net);
                    }
                }

                var error = ValidateDelta(delta.Item, net, delta.Amount, out _);
                if (error != ItemError.None)
                {
                    failedIndex = i;
                    return error;
                }
            }

            for (var i = 0; i < deltas.Length; i++)
            {
                var delta = deltas[i];
                Ops.TryAdd(GetQuantity(delta.Item), delta.Amount, out var result);
                SetOrRemove(delta.Item, result);
            }

            _onChanged?.Invoke();
            return ItemError.None;
        }

        /// <summary>
        /// 다른 컨테이너로 수량을 원자적으로 이동한다. 소스 부족·대상 상한을 모두
        /// 선검증한 뒤 반영하며, 성공 시 양쪽 onChanged가 각 1회 호출된다.
        /// 두 컨테이너는 같은 카탈로그여야 한다(다르면 구성 오류로 던진다).
        /// </summary>
        public ItemError TryMoveTo(ItemStackContainer<TQuantity, TOps> target, ItemId item, TQuantity amount)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (!ReferenceEquals(target._catalog, _catalog))
            {
                throw new ArgumentException("이동은 같은 카탈로그의 컨테이너 사이에서만 가능합니다.", nameof(target));
            }

            if (!_catalog.Contains(item))
            {
                return ItemError.UnknownItem;
            }

            if (Ops.Compare(amount, Ops.Zero) <= 0)
            {
                return ItemError.InvalidAmount;
            }

            var sourceError = ValidateDelta(item, GetQuantity(item), Ops.Negate(amount), out var sourceResult);
            if (sourceError != ItemError.None)
            {
                return sourceError;
            }

            // 자기 자신으로의 이동은 소스 반영 후 잔량 기준으로 판정해 순 무변경이 된다.
            var targetCurrent = ReferenceEquals(target, this) ? sourceResult : target.GetQuantity(item);
            var targetError = ValidateDelta(item, targetCurrent, amount, out var targetResult);
            if (targetError != ItemError.None)
            {
                return targetError;
            }

            if (ReferenceEquals(target, this))
            {
                return ItemError.None;
            }

            SetOrRemove(item, sourceResult);
            target.SetOrRemove(item, targetResult);
            _onChanged?.Invoke();
            target._onChanged?.Invoke();
            return ItemError.None;
        }

        /// <summary>
        /// 저장 로드용 — 수량을 직접 설정하고 onChanged를 호출하지 않는다.
        /// maxStack 검사는 수행한다(정의 변경으로 상한이 줄었으면 실패를 보고
        /// 게임이 정책을 결정). quantity는 양수여야 한다.
        /// </summary>
        public ItemError TryLoad(ItemId item, TQuantity quantity)
        {
            if (!_catalog.Contains(item))
            {
                return ItemError.UnknownItem;
            }

            if (Ops.Compare(quantity, Ops.Zero) <= 0)
            {
                return ItemError.InvalidAmount;
            }

            var maxStack = _catalog.GetMaxStack(item);
            if (maxStack != long.MaxValue && Ops.Compare(quantity, Ops.FromLong(maxStack)) > 0)
            {
                return ItemError.ExceedsMaxStack;
            }

            _stacks[item] = quantity;
            return ItemError.None;
        }

        /// <summary>모든 스택을 비운다. 비어 있지 않았으면 onChanged 1회.</summary>
        public void Clear()
        {
            if (_stacks.Count == 0)
            {
                return;
            }

            _stacks.Clear();
            _onChanged?.Invoke();
        }

        /// <summary>보유 스택 열거 — foreach 무할당(struct 열거자). 저장 직렬화는 이 열거로 한다.</summary>
        public Enumerator GetEnumerator() => new Enumerator(_stacks.GetEnumerator());

        /// <summary>current + delta의 결과를 상한·부족·오버플로 순으로 판정한다.</summary>
        private ItemError ValidateDelta(ItemId item, TQuantity current, TQuantity delta, out TQuantity result)
        {
            if (!Ops.TryAdd(current, delta, out result))
            {
                return ItemError.ExceedsMaxStack;
            }

            if (Ops.Compare(result, Ops.Zero) < 0)
            {
                return ItemError.Insufficient;
            }

            var maxStack = _catalog.GetMaxStack(item);
            if (maxStack != long.MaxValue && Ops.Compare(result, Ops.FromLong(maxStack)) > 0)
            {
                return ItemError.ExceedsMaxStack;
            }

            return ItemError.None;
        }

        private void SetOrRemove(ItemId item, TQuantity quantity)
        {
            if (Ops.Compare(quantity, Ops.Zero) == 0)
            {
                _stacks.Remove(item);
            }
            else
            {
                _stacks[item] = quantity;
            }
        }

        /// <summary>딕셔너리 struct 열거자를 감싸 <see cref="ItemStack{TQuantity}"/>를 내놓는다.</summary>
        public struct Enumerator
        {
            private Dictionary<ItemId, TQuantity>.Enumerator _inner;

            internal Enumerator(Dictionary<ItemId, TQuantity>.Enumerator inner)
            {
                _inner = inner;
            }

            /// <summary>현재 스택.</summary>
            public ItemStack<TQuantity> Current
            {
                get
                {
                    var entry = _inner.Current;
                    return new ItemStack<TQuantity>(entry.Key, entry.Value);
                }
            }

            /// <summary>다음 스택으로 이동한다.</summary>
            public bool MoveNext() => _inner.MoveNext();
        }
    }
}
