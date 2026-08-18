using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 시간 경과 lazy 정산 공식 — 티켓류 아이템·스태미나의 "접근 시점에 몰아서 충전"
    /// 패턴(idlez CalculateRegenValueFromPeriod·growninja RefreshRegenItem의 공통 코어).
    /// 타이머 없이 게임이 현재 시각을 주입해 호출한다. 수량 반영은 게임 몫:
    /// <code>
    /// var granted = Regen.SettlePeriodic(count, max, period, now, ref state.RegenRefreshTicks);
    /// if (granted &gt; 0) { inventory.TryAdd(ticketId, granted); instance.MarkChanged(); }
    /// </code>
    /// </summary>
    public static class Regen
    {
        /// <summary>
        /// 주기당 1개 정산. 소비한 주기만큼만 기준 시각을 전진시켜 나머지 경과를
        /// 보존한다(연속 호출에도 드리프트 없음 — idlez의 옛 구현이 틀렸던 지점).
        /// 초당 r개 연속 리젠은 period = 1초/r 로 환산해 쓴다.
        /// </summary>
        /// <param name="currentCount">현재 보유 수량.</param>
        /// <param name="maxCount">리젠 상한 — 이미 도달했으면 0을 주고 기준 시각을 현재로
        /// 재설정한다(가득 상태에서 경과 시간 적립 방지).</param>
        /// <param name="periodTicks">1개 충전에 걸리는 시간(ticks, 양수).</param>
        /// <param name="nowTicksUtc">현재 시각(UTC ticks) — 게임 주입.</param>
        /// <param name="lastRegenTicksUtc">기준 시각. 0(미초기화)이면 현재로 초기화하고
        /// 0을 준다 — "최초 정산에서 가득 차는" 고전 버그 방지. 미래 값(시계 역행)도
        /// 현재로 재설정하고 0을 준다.</param>
        /// <returns>이번 정산에서 충전된 수량(0 이상).</returns>
        public static long SettlePeriodic(
            long currentCount,
            long maxCount,
            long periodTicks,
            long nowTicksUtc,
            ref long lastRegenTicksUtc)
        {
            if (periodTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(periodTicks), periodTicks, "주기는 양수여야 합니다.");
            }

            if (lastRegenTicksUtc == 0 || lastRegenTicksUtc > nowTicksUtc)
            {
                lastRegenTicksUtc = nowTicksUtc;
                return 0;
            }

            if (currentCount >= maxCount)
            {
                lastRegenTicksUtc = nowTicksUtc;
                return 0;
            }

            var elapsedPeriods = (nowTicksUtc - lastRegenTicksUtc) / periodTicks;
            var granted = Math.Min(maxCount - currentCount, elapsedPeriods);
            if (granted <= 0)
            {
                return 0;
            }

            if (currentCount + granted >= maxCount)
            {
                lastRegenTicksUtc = nowTicksUtc;   // 가득 도달 — 남은 적립 제거
            }
            else
            {
                lastRegenTicksUtc += periodTicks * granted;
            }

            return granted;
        }
    }
}
