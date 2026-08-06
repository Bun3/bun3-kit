using System;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>SteamSessionVerifier 옵션 — 네이티브 호출 2개를 게임이 델리게이트로 꽂는다
    /// (프레임워크는 Steamworks C# 바인딩에 의존하지 않는다). 생성자에서 검증·스냅샷된다.</summary>
    public sealed class SteamSessionOptions
    {
        /// <summary>SteamUser.BeginAuthSession 래핑 — (티켓 바이트, 주장 SteamID64)를 받아
        /// 즉시 결과 코드(EBeginAuthSessionResult, 0=OK)를 돌려준다. 필수.</summary>
        public Func<byte[], ulong, int>? BeginSession { get; set; }

        /// <summary>SteamUser.EndAuthSession 래핑. 필수.</summary>
        public Action<ulong>? EndSession { get; set; }

        /// <summary>ValidateAuthTicketResponse 콜백 대기 한도 — 초과 시 AuthFailure.Timeout 값으로 실패.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}
