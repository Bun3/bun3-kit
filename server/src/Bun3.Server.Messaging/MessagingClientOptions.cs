using System;

namespace Bun3.Server.Messaging
{
    /// <summary>MessagingClient의 동작을 조정하는 옵션.</summary>
    public sealed class MessagingClientOptions
    {
        /// <summary>요청별 응답 대기 기한. 초과 시 해당 요청만 TimeoutException.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Ping 주기. null = 비활성.</summary>
        public TimeSpan? PingInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>true면 접속 시점의 SynchronizationContext로 푸시 콜백·Closed 이벤트를 올린다(Unity 메인 스레드).</summary>
        public bool UseSynchronizationContext { get; set; } = true;
    }
}
