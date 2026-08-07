using System;

namespace Bun3.Server.Ticking
{
    /// <summary>TickLoop 동작 옵션 — 생성자에서 스냅샷되며 이후 변경은 무시된다.</summary>
    public sealed class TickingOptions
    {
        /// <summary>루프 1회전 목표 주기. 잡 실행 시간을 빼고 대기한다(드리프트 보정).</summary>
        public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>시계 — 기본 시스템 시계. 테스트/특수 환경에서 교체 가능.</summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    }
}
