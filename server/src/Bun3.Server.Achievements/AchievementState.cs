namespace Bun3.Server.Achievements
{
    /// <summary>
    /// 업적 1개의 플레이어 상태. 저장은 게임 몫이라 직렬화하기 쉬운 public 필드
    /// struct로 둔다 — 게임은 <see cref="AchievementTracker{TDef}.GetState"/>로 읽어
    /// 저장하고, 로드 시 <see cref="AchievementTracker{TDef}.Restore"/>로 복원한다.
    /// </summary>
    public struct AchievementState
    {
        /// <summary>누적 진행도. 반복 업적은 무한 누적, 비반복 업적은 목표치에 클램프.</summary>
        public long Progress;

        /// <summary>달성 횟수. 단조 증가하며 감소하지 않는다(중복 달성 방지의 근거).
        /// 비반복 업적은 최대 1.</summary>
        public int CompletedCount;

        /// <summary>보상 수령 횟수. 항상 <see cref="CompletedCount"/> 이하.</summary>
        public int ClaimedCount;

        /// <summary>마지막 달성 시각(UTC ticks). 0이면 미달성.</summary>
        public long LastCompletedAtUtcTicks;
    }
}
