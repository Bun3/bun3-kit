namespace Bun3.Server.Auth.Steam
{
    /// <summary>SteamWebApiVerifier 옵션 — 생성자에서 검증·스냅샷되며 이후 변경은 무시된다.</summary>
    public sealed class SteamWebApiOptions
    {
        /// <summary>Steam AppId. 0이면 생성자에서 거부.</summary>
        public uint AppId { get; set; }

        /// <summary>Publisher Web API Key — 서버 비밀. 환경변수/설정으로만 주입하고 커밋 금지.</summary>
        public string WebApiKey { get; set; } = "";

        /// <summary>GetAuthTicketForWebApi("identity")로 발급한 티켓이면 같은 identity 문자열 — 쿼리에 동봉된다.</summary>
        public string? Identity { get; set; }

        /// <summary>VAC 밴을 위조/만료와 동일하게 실패 처리(기본). 끄면 성공 + 플래그.</summary>
        public bool RejectVacBanned { get; set; } = true;

        /// <summary>퍼블리셔 밴을 실패 처리(기본). 끄면 성공 + 플래그.</summary>
        public bool RejectPublisherBanned { get; set; } = true;
    }
}
