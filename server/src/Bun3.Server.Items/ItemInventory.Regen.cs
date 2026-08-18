using System.Collections.Generic;

namespace Bun3.Server.Items
{
    // 리젠 자동 정산 — 카탈로그에 regenPeriodTicks가 등록된 정의를 일괄 처리한다.
    // 기준 시각은 인스턴스가 아니라 정의별 맵에 둔다 — 수량 0으로 인스턴스가 사라져도
    // 리젠이 계속되는 idlez 티켓 의미론(0개에서 다시 차오름).
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>
        /// 리젠 메타가 있는 모든 정의를 현재 시각으로 lazy 정산한다 — 충전분은 한 배치로
        /// 원자 지급되며(onChanged·onApplied 각 1회) 충전된 정의 수를 반환한다.
        /// 게임은 접속·틱·소모 직전 등 접근 지점에서 호출하면 된다(타이머 불필요).
        /// 소모 전에 정산하는 것이 계약이다 — 가득 상태의 기준 재설정이 적립 악용을 막는다.
        /// </summary>
        /// <param name="nowTicksUtc">현재 시각(UTC ticks) — 게임 주입.</param>
        public int SettleRegen(long nowTicksUtc)
        {
            var regenItems = _catalog.RegenItems;
            if (regenItems.Length == 0)
            {
                return 0;
            }

            _applyOps.Clear();
            _regenScratchItems.Clear();
            _regenScratchBasis.Clear();
            for (var i = 0; i < regenItems.Length; i++)
            {
                var item = regenItems[i];
                _regenBasis.TryGetValue(item, out var basis);
                var maxStack = _catalog.GetMaxStack(item);
                var quantity = GetQuantity(item);
                if (quantity.CompareTo(maxStack) >= 0)
                {
                    // 목표선 이상(초과 보유 포함) — 리젠 정지, 적립 제거. long 변환 없이 판정
                    // (목표선 초과 수량은 long 범위를 넘을 수 있다).
                    _regenBasis[item] = nowTicksUtc;
                    continue;
                }

                var current = ToInt64Exact(quantity);
                var granted = Regen.SettlePeriodic(
                    current, maxStack, _catalog.GetRegenPeriodTicks(item), nowTicksUtc, ref basis);
                if (granted > 0)
                {
                    // 지급 성공 후에만 기준을 전진시킨다 — 실패 시 다음 정산에서 재시도.
                    _applyOps.Add(new TxOp(TxOpKind.Add, item, 0, granted));
                    _regenScratchItems.Add(item);
                    _regenScratchBasis.Add(basis);
                }
                else
                {
                    _regenBasis[item] = basis;   // 초기화·가득 재설정도 기록
                }
            }

            if (_applyOps.Count == 0)
            {
                return 0;
            }

            if (CommitOps(_applyOps, out _, null) != InventoryError.None)
            {
                return 0;   // 도달 불가 방어 — 기준 미전진으로 유실 없음
            }

            for (var i = 0; i < _regenScratchItems.Count; i++)
            {
                _regenBasis[_regenScratchItems[i]] = _regenScratchBasis[i];
            }

            return _regenScratchItems.Count;
        }

        /// <summary>정의의 리젠 기준 시각(마지막 충전 완성 시각). 미기록이면 0.
        /// 저장은 <see cref="ItemCatalog.RegenItems"/> 순회로 정의별 기준을 함께 영속화한다.</summary>
        public long GetRegenBasis(ItemId item)
        {
            _regenBasis.TryGetValue(item, out var basis);
            return basis;
        }

        /// <summary>저장 로드용 — 리젠 기준 시각을 복원한다(추적·통지 없음).</summary>
        public void LoadRegenBasis(ItemId item, long lastRegenTicksUtc)
        {
            if (!_catalog.Contains(item))
            {
                throw new System.ArgumentOutOfRangeException(nameof(item), "이 카탈로그의 식별자가 아닙니다.");
            }

            _regenBasis[item] = lastRegenTicksUtc;
        }

        /// <summary>정수 수량을 long으로 변환한다 — 리젠 정의는 정수 수량이 강제되고
        /// maxStack(long) 이하이므로 정확 변환이 보장된다.</summary>
        private static long ToInt64Exact(Bun3.Gameplay.Numerics.BigNum value)
        {
            var result = value.Mantissa;
            for (var i = 0; i < value.Exponent; i++)
            {
                result *= 10;
            }

            return result;
        }
    }
}
