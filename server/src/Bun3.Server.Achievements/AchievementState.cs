namespace Bun3.Server.Achievements
{
    /// <summary>
    /// 업적 1개의 플레이어 상태. 저장은 게임 몫이라 직렬화하기 쉬운 public 필드
    /// struct로 둔다 — 게임은 <see cref="AchievementTracker{TDef}.GetState"/>로 읽어
    /// 저장하고, 로드 시 <see cref="AchievementTracker{TDef}.Restore"/>로 복원한다.
    /// Completed/Claimed 상태는 여기 저장되지 않는다 — 카운터에서 파생된다.
    /// </summary>
    public struct AchievementState
    {
        /// <summary>누적 진행도. 반복 업적은 수령 시 목표치만큼 차감되며 수령 대기 중에는
        /// 목표치 이상으로 쌓여 있다(UI는 min(Progress, Target)/Target으로 표시).
        /// 비반복 업적은 목표치에 클램프.</summary>
        public long Progress;

        /// <summary>달성 횟수. 단조 증가하며 감소하지 않는다(중복 달성 방지의 근거).
        /// 비반복 업적은 최대 1. 반복 업적 불변식: CompletedCount = ClaimedCount + Progress/Target.</summary>
        public int CompletedCount;

        /// <summary>보상 수령 횟수. 항상 <see cref="CompletedCount"/> 이하.</summary>
        public int ClaimedCount;

        /// <summary>마지막 달성 시각(UTC ticks). 0이면 미달성.</summary>
        public long LastCompletedAtUtcTicks;

        /// <summary>가용성 — Locked/Ready/Active만 유효(Restore가 검증). 진행은 Active에서만
        /// 쌓이고, 전이는 <see cref="AchievementTracker{TDef}"/>의 Unlock/Activate/Lock으로만 한다.</summary>
        public AchievementStatus Availability;
    }
}
