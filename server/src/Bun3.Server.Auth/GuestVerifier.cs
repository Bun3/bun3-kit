using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth
{
    /// <summary>게스트 검증기 — 검증할 자격증명이 없는 대신 신뢰 경계 검증(형식)만 수행한다.
    /// credential = 클라이언트 device-id. 허용 문자는 영숫자와 <c>- _ .</c>뿐이다 —
    /// 제어문자·개행이 AccountKey를 타고 로그/DB 키로 흘러드는 인젝션을 여기서 차단한다.
    /// 본질은 클라 주장 신뢰이며, 같은 계약 뒤에 두는 이유는 Steam 등으로 전환할 때
    /// 로그인 핸들러가 검증기 한 줄만 바뀌게 하기 위함.</summary>
    public sealed class GuestVerifier : IIdentityVerifier
    {
        /// <summary>device-id 최대 길이.</summary>
        public const int MaxSubjectLength = 128;

        /// <inheritdoc />
        public string Provider => "guest";

        /// <inheritdoc />
        public ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default)
        {
            var subject = credential?.Trim() ?? string.Empty;
            if (subject.Length == 0 || subject.Length > MaxSubjectLength || !IsValidSubject(subject))
                return new ValueTask<AuthResult>(AuthResult.Fail(AuthFailure.InvalidCredential, "invalid device id"));

            return new ValueTask<AuthResult>(AuthResult.Success(new ProviderIdentity(Provider, subject)));
        }

        // 화이트리스트 검증 — ':'(provider 구분자)를 포함한 그 밖의 모든 문자를 거부한다.
        private static bool IsValidSubject(string subject)
        {
            foreach (var c in subject)
            {
                var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                    || c == '-' || c == '_' || c == '.';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
