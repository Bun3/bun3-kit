using System;

namespace Bun3.Server.Messaging
{
    /// <summary>MessagingServer 기동 옵션.</summary>
    public sealed class MessagingServerOptions
    {
        /// <summary>
        /// 이 시간 동안 아무 패킷도 안 온 세션을 킥한다. null = 비활성.
        /// 수신 시각은 패킷 '처리 시작' 기준이므로, 이 값보다 오래 걸리는 핸들러는 자기 세션을 킥할 수 있다 — 타임아웃을 줄일 때 주의.
        /// </summary>
        public TimeSpan? IdleKickTimeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>세션 수신 큐 상한 (v0 Session과 동일 의미).</summary>
        public int MaxQueuedPackets { get; set; } = 256;
    }
}
