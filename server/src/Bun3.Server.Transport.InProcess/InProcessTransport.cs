using System;
using System.Threading;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Transport.InProcess
{
    /// <summary>
    /// 소켓 없는 인프로세스(루프백) 전송의 페어 팩토리. 하나의 인스턴스가 리스너와 커넥터
    /// 양쪽을 발급하며, <see cref="Connector"/>의 ConnectAsync마다 <see cref="Listener"/>에
    /// 새 연결 페어가 붙는다. 순서·소유권·백프레셔·종료 의미론은 Transport.Tcp와 동일하다.
    /// 용도: 클라호스트의 호스트 플레이어 자기접속, 실TCP 없는 서버 E2E 테스트.
    /// </summary>
    public sealed class InProcessTransport
    {
        private static long s_nextConnectionId; // 프로세스 전역 단조 증가 — IConnection.Id 계약

        /// <summary>
        /// 인프로세스 전송 페어를 구성한다.
        /// </summary>
        /// <param name="maxQueuedPacketsPerConnection">끝점당 수신 인박스 용량. 가득 차면
        /// 송신자가 대기한다(TCP 소켓 버퍼 백프레셔에 해당).</param>
        /// <param name="logger">선택 로거. 서버 OnConnected 실패 같은 저빈도 경로만 기록한다.</param>
        public InProcessTransport(int maxQueuedPacketsPerConnection = 256, ILogger? logger = null)
        {
            if (maxQueuedPacketsPerConnection <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxQueuedPacketsPerConnection));
            }

            var listener = new InProcessListener();
            Listener = listener;
            Connector = new InProcessConnector(
                listener, maxQueuedPacketsPerConnection, new SafeLogger(logger ?? NullLogger.Instance));
        }

        /// <summary>수락 측. StartAsync(serverHandler)로 수락을 열고, StopAsync 후 Connect는 실패한다.</summary>
        public ITransportListener Listener { get; }

        /// <summary>접속 측. ConnectAsync(clientHandler) 호출마다 새 연결 페어를 만든다.</summary>
        /// <remarks>ConnectAsync는 서버 핸들러의 OnConnected를 호출 스레드에서 동기 실행한다
        /// (결정적 순서 보장). 서버 OnConnected가 블록하면 ConnectAsync도 블록되므로,
        /// OnConnected는 계약대로 빠르게 반환해야 한다.</remarks>
        public IConnector Connector { get; }

        internal static long NextConnectionId() => Interlocked.Increment(ref s_nextConnectionId);
    }
}
