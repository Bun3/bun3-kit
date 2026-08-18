namespace Bun3.Server.Achievements
{
    /// <summary>
    /// 업적 수명주기 상태. 저장되는 것은 가용성 셋(Locked/Ready/Active)뿐이며 —
    /// <see cref="AchievementState.Availability"/> — Completed/Claimed는
    /// <see cref="AchievementManager{TDef}.GetStatus"/>가 카운터에서 파생한다(저장 안 함).
    /// 숫자 값은 저장 포맷의 일부이므로 바꾸지 않는다.
    /// </summary>
    public enum AchievementStatus
    {
        /// <summary>닫힘 — 진행 불가. 컨텐츠/기능 게이트의 "잠김", 로테이션 제외 상태.</summary>
        Locked = 0,

        /// <summary>열렸지만 시작 전 — 목록에 보이나 진행이 쌓이지 않는다(일일 미션 선발 등).</summary>
        Ready = 1,

        /// <summary>진행 수용 중.</summary>
        Active = 2,

        /// <summary>파생 전용 — 수령하지 않은 달성이 있다(수령 가능 건수 &gt; 0).</summary>
        Completed = 3,

        /// <summary>파생 전용 — 비반복 업적을 달성하고 전부 수령한 종결 상태.</summary>
        Claimed = 4,
    }
}
