using System;

namespace Bun3.Server.Players
{
    /// <summary>같은 계정이 접속 중일 때 새 로그인의 처리.</summary>
    public enum DuplicateLoginPolicy
    {
        /// <summary>기존 연결을 킥하고 새 세션에 재바인딩 (기본).</summary>
        NewWins,

        /// <summary>새 로그인을 거부 — SignInAsync가 DuplicateLoginException을 던진다.</summary>
        RejectNew,
    }

    /// <summary>PlayerRegistry 동작 옵션. 호스팅(AddPlayerServer)은 구성 섹션
    /// "Bun3:Players"에서 바인딩한 뒤 람다를 적용한다.</summary>
    public sealed class PlayersOptions
    {
        /// <summary>구성 바인딩에 사용되는 섹션 이름.</summary>
        public const string SectionName = "Bun3:Players";

        /// <summary>연결이 끊긴 Player를 메모리에 유지하는 재접속 유예. Zero면 즉시 은퇴.</summary>
        public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>중복 로그인 처리 정책.</summary>
        public DuplicateLoginPolicy DuplicatePolicy { get; set; } = DuplicateLoginPolicy.NewWins;

        /// <summary>접속 중 Player의 OnTickAsync 호출 주기.</summary>
        public TimeSpan PlayerTickInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>dirty Player의 주기 저장 간격 — 크래시 시 손실 상한.</summary>
        public TimeSpan SaveInterval { get; set; } = TimeSpan.FromSeconds(30);
    }
}
