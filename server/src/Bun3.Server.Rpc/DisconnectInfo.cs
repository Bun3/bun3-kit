using System;

namespace Bun3.Server.Rpc
{
    /// <summary>절단 통지 페이로드. Code 0 = Disconnect 미수신(네트워크 절단/자발적 Close) —
    /// 수신 = 의도된 킥(안내 UI), 미수신 = 사고(재접속 루트)의 분기점.</summary>
    public readonly struct DisconnectInfo
    {
        /// <summary>절단 사유 — 1~99 프레임워크(DisconnectCode), 음수 게임 정의, 0 미수신.</summary>
        public int Code { get; }

        /// <summary>전송 계층 오류(있으면).</summary>
        public Exception? Error { get; }

        /// <summary>사유가 전달되었는지 여부.</summary>
        public bool HasReason => Code != 0;

        /// <summary>통지 페이로드를 생성한다.</summary>
        public DisconnectInfo(int code, Exception? error)
        {
            Code = code;
            Error = error;
        }
    }
}
