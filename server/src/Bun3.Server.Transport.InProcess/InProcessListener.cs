using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Transport.InProcess
{
    /// <summary>인프로세스 리스너. 소켓이 없으므로 수락 루프 없이 핸들러 등록만 한다.</summary>
    internal sealed class InProcessListener : ITransportListener
    {
        private volatile IConnectionHandler? _handler;
        private volatile bool _stopped;
        private int _started;

        /// <remarks>단일 사용: StopAsync 이후 재시작할 수 없다(Tcp 리스너와 동일).</remarks>
        public Task StartAsync(IConnectionHandler handler, CancellationToken ct = default)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                throw new InvalidOperationException("Listener is already started.");
            }

            _handler = handler;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _stopped = true; // 신규 수락만 중단 — 기존 연결 종료는 상위 책임(계약)
            return Task.CompletedTask;
        }

        /// <summary>수락 가능하면 서버 핸들러, 미시작/중지면 null.</summary>
        internal IConnectionHandler? TryGetAcceptHandler() => _stopped ? null : _handler;
    }
}
