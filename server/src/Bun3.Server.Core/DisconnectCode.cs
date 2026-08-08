namespace Bun3.Server.Core
{
    /// <summary>절단 사유 코드 — 1~99 프레임워크 예약, 음수 게임 정의 (Reply.Status와 동일 대역 규약).
    /// Core에 두는 이유: 킥 발생 지점이 Core(큐 초과·drain)/Rpc(idle·위반)/Players(중복 로그인)에 걸쳐 있다.</summary>
    public static class DisconnectCode
    {
        /// <summary>클라 전용 의미 — Disconnect 미수신 절단(네트워크/자발적 Close). 와이어에 싣지 않는다.</summary>
        public const int None = 0;

        /// <summary>서버 정지 drain.</summary>
        public const int ServerShutdown = 1;

        /// <summary>중복 로그인(NewWins) — 다른 기기에서 로그인.</summary>
        public const int DuplicateLogin = 2;

        /// <summary>idle 타임아웃.</summary>
        public const int IdleKick = 3;

        /// <summary>세션 큐 초과 킥.</summary>
        public const int QueueOverflow = 4;

        /// <summary>Rpc 계층이 판정한 프로토콜 위반(미지 채널, 파싱 실패 등).
        /// 전송 계층 절단(패킷 크기 초과)은 사유 전달 불가.</summary>
        public const int ProtocolViolation = 5;
    }
}
