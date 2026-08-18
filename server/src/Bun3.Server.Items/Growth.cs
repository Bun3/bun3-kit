using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 경험치 → 레벨 정산 공식 — idlez RefreshItemExp·growninja RefreshLevelEXP가 각자
    /// 구현한 "필요치 테이블 소진 다단계 레벨업 루프"의 공통 코어. 수치 반영(TState 저장,
    /// MarkChanged, 레벨업 이벤트)은 게임 몫이다. 레벨 직접 증가·리셋은 대입으로 충분해
    /// 프레임워크가 제공하지 않는다.
    /// </summary>
    public static class Growth
    {
        /// <summary>
        /// 누적 경험치를 레벨로 정산한다 — 여러 레벨을 한 번에 오를 수 있고, 소진하고
        /// 남은 경험치는 <paramref name="exp"/>에 보존된다. 만렙에 도달하면 멈추며 잔여
        /// 경험치는 그대로 남는다(버릴지는 호출측 정책).
        /// </summary>
        /// <param name="level">현재 레벨(ref — 정산 후 갱신).</param>
        /// <param name="exp">누적 경험치(ref — 소진 후 잔여 보존).</param>
        /// <param name="maxLevel">만렙 — 이 레벨에 도달하면 더 오르지 않는다.</param>
        /// <param name="requiredExpForNext">해당 레벨에서 다음 레벨로 가는 필요 경험치
        /// (양수 필수 — 0 이하는 데이터 오류로 던진다). 게임의 경험치 테이블 조회 시섬.</param>
        /// <returns>이번 정산에서 오른 레벨 수(0 이상).</returns>
        public static int SettleExp(ref int level, ref long exp, int maxLevel, Func<int, long> requiredExpForNext)
        {
            if (requiredExpForNext == null)
            {
                throw new ArgumentNullException(nameof(requiredExpForNext));
            }

            var gained = 0;
            while (level < maxLevel)
            {
                var required = requiredExpForNext(level);
                if (required <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(requiredExpForNext), required, $"레벨 {level}의 필요 경험치는 양수여야 합니다.");
                }

                if (exp < required)
                {
                    break;
                }

                exp -= required;
                level++;
                gained++;
            }

            return gained;
        }
    }
}
