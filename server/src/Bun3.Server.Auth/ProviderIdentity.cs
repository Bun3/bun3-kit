namespace Bun3.Server.Auth
{
    /// <summary>제공자 검증을 통과한 신원 — (제공자, 제공자 내 고유 id) 쌍.</summary>
    public readonly struct ProviderIdentity
    {
        /// <summary>제공자 이름(소문자 규약) — "guest", "steam" 등.</summary>
        public string Provider { get; }

        /// <summary>제공자 내 고유 id — SteamID64, device-id 등.</summary>
        public string Subject { get; }

        /// <summary>신원을 생성한다.</summary>
        public ProviderIdentity(string provider, string subject)
        {
            Provider = provider;
            Subject = subject;
        }

        /// <summary>Players 권장 규약("provider:subject")의 accountKey 문자열을 만든다.
        /// 계정 연동을 쓰는 게임은 이 값 대신 연동 테이블 조회 결과("acct:{id}")를 쓴다.</summary>
        public string ToAccountKey() => $"{Provider}:{Subject}";
    }
}
