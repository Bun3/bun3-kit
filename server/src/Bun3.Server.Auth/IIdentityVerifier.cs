using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth
{
    /// <summary>제공자별 자격증명 검증기 — Players 신원 모델의 1층.
    /// 게임 로그인 핸들러가 호출하고, 성공 신원으로 accountKey를 만들어 SignInAsync에 넘긴다.</summary>
    public interface IIdentityVerifier
    {
        /// <summary>제공자 이름(소문자 규약) — 발급하는 ProviderIdentity.Provider와 일치한다.</summary>
        string Provider { get; }

        /// <summary>자격증명을 검증한다. 거절은 실패 값, 인프라 문제는 예외.
        /// credential 인코딩은 제공자 정의(각 검증기 문서 참고).</summary>
        ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default);
    }
}
