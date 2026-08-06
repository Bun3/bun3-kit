namespace Bun3.Server.Auth
{
    /// <summary>검증 실패 사유 — 제공자와 무관한 공통 어휘. 게임은 이 값을 자기 proto 에러코드로 매핑한다.</summary>
    public enum AuthFailure
    {
        /// <summary>실패 아님(성공).</summary>
        None = 0,

        /// <summary>자격증명 형식 불량 — 빈 device-id, hex 파싱 실패, 규약 위반.</summary>
        InvalidCredential = 1,

        /// <summary>제공자가 거절 — 위조/만료 티켓, 무효 토큰.</summary>
        Rejected = 2,

        /// <summary>제공자 밴 — VAC/퍼블리셔 밴(거절 옵션이 켜진 경우).</summary>
        Banned = 3,

        /// <summary>예약 — 주장한 신원과 검증 결과 불일치(현 구현 미발생, 미래 제공자용).</summary>
        IdentityMismatch = 4,

        /// <summary>검증 응답 시간 초과(네이티브 콜백 미도착 등).</summary>
        Timeout = 5,
    }
}
