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

    /// <summary>PlayerRegistry 동작 옵션.</summary>
    public sealed class PlayersOptions
    {
        /// <summary>연결이 끊긴 Player를 메모리에 유지하는 재접속 유예. Zero면 즉시 은퇴.</summary>
        public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>중복 로그인 처리 정책.</summary>
        public DuplicateLoginPolicy DuplicatePolicy { get; set; } = DuplicateLoginPolicy.NewWins;
    }
}
