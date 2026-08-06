using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth
{
    /// <summary>게스트 검증기 — 검증할 자격증명이 없는 대신 신뢰 경계 검증(형식)만 수행한다.
    /// credential = 클라이언트 device-id. 본질은 클라 주장 신뢰이며, 같은 계약 뒤에 두는
    /// 이유는 Steam 등으로 전환할 때 로그인 핸들러가 검증기 한 줄만 바뀌게 하기 위함.</summary>
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
            if (subject.Length == 0 || subject.Length > MaxSubjectLength || subject.Contains(':'))
                return new ValueTask<AuthResult>(AuthResult.Fail(AuthFailure.InvalidCredential, "invalid device id"));

            return new ValueTask<AuthResult>(AuthResult.Success(new ProviderIdentity(Provider, subject)));
        }
    }
}
