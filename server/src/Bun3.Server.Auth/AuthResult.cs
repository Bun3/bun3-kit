namespace Bun3.Server.Auth
{
    /// <summary>검증 판정 — 예상 가능한 거절은 값으로, 인프라 실패만 예외로 표면화된다.</summary>
    public class AuthResult
    {
        /// <summary>검증 성공 여부.</summary>
        public bool Succeeded { get; }

        /// <summary>검증된 신원 — 성공 시에만 유효.</summary>
        public ProviderIdentity Identity { get; }

        /// <summary>실패 사유 — 실패 시에만 유효.</summary>
        public AuthFailure Failure { get; }

        /// <summary>로그용 설명 — 와이어에 싣지 말 것.</summary>
        public string? Error { get; }

        /// <summary>파생 결과 타입(제공자별 디테일)용 생성자.</summary>
        protected AuthResult(bool succeeded, ProviderIdentity identity, AuthFailure failure, string? error)
        {
            Succeeded = succeeded;
            Identity = identity;
            Failure = failure;
            Error = error;
        }

        /// <summary>성공 판정을 만든다.</summary>
        public static AuthResult Success(ProviderIdentity identity) =>
            new AuthResult(true, identity, AuthFailure.None, null);

        /// <summary>실패 판정을 만든다.</summary>
        public static AuthResult Fail(AuthFailure failure, string? error = null) =>
            new AuthResult(false, default, failure, error);
    }
}
