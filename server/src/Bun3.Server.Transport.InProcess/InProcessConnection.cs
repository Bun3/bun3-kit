using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Transport.InProcess
{
    internal sealed class InProcessConnection : IConnection
    {
        // 종료(FIN) 센티널 — 참조 비교 전용. 사용자 패킷은 항상 복사된 새 배열(빈 패킷은
        // Array.Empty)이라 이 인스턴스와 절대 겹치지 않는다.
        private static readonly byte[] ClosedByPeerSentinel = new byte[1];

        // 닫힐 때 슬롯 세마포어에 방출해 백프레셔로 대기 중인 송신자를 전부 깨우는 수량.
        // 현실적인 동시 대기자 수보다 충분히 크고, Close의 CAS로 1회만 방출되므로 오버플로 없다.
        private const int WakeAllSenders = 1 << 20;

        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _items = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _slots; // 유한 인박스 — TCP 소켓 버퍼의 백프레셔에 해당
        private readonly IConnectionHandler _handler;
        private InProcessConnection _peer = null!; // 페어 생성 직후 Link로 반드시 연결된다
        private int _closed; // 0 = open, 1 = closed

        internal InProcessConnection(long id, int maxQueuedPackets, IConnectionHandler handler)
        {
            Id = id;
            _handler = handler;
            _slots = new SemaphoreSlim(maxQueuedPackets);
        }

        public long Id { get; }

        public string? RemoteAddress => "inproc";

        public bool IsOpen => Volatile.Read(ref _closed) == 0;

        internal void Link(InProcessConnection peer) => _peer = peer;

        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default)
        {
            if (!IsOpen)
            {
                return default; // 계약: 닫힌 연결에 대한 송신은 no-op
            }

            return _peer.EnqueueFromPeerAsync(packet, ct);
        }

        private async ValueTask EnqueueFromPeerAsync(ReadOnlyMemory<byte> packet, CancellationToken ct)
        {
            if (!IsOpen)
            {
                return; // 수신 측이 이미 닫힘 — 닫힌 소켓으로 사라지는 바이트처럼 드롭
            }

            await _slots.WaitAsync(ct).ConfigureAwait(false);
            if (!IsOpen)
            {
                return; // 대기 중 닫힘(WakeAllSenders 방출로 깨어난 경우 포함) — 드롭
            }

            // 소유권 계약: OnPacket의 배열은 수신자 소유가 되므로 송신자 버퍼를 여기서 1회 복사한다.
            // (Tcp도 수신 패킷당 새 배열 1개를 할당하므로 패킷당 할당 수는 동일)
            _inbox.Enqueue(packet.ToArray());
            _items.Release();
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return; // 멱등
            }

            _items.Release();               // 자기 펌프를 깨워 종료·OnClosed 통지로 잇는다
            _slots.Release(WakeAllSenders); // 이 인박스에 블록된 상대 송신자를 전부 깨운다
            _peer.NotifyPeerClosed();
        }

        private void NotifyPeerClosed()
        {
            // FIN에 해당 — 큐 꼬리에 넣어 먼저 큐잉된 패킷이 전부 전달(드레인)된 뒤 닫히게 한다
            _inbox.Enqueue(ClosedByPeerSentinel);
            _items.Release();
        }

        /// <summary>
        /// 수신 펌프. 연결당 1개, 해당 끝점의 OnConnected 반환 후에만 시작한다.
        /// 종료 시 OnClosed를 정확히 1회 통지한다(이 finally가 유일한 통지 지점).
        /// </summary>
        internal async Task RunReceivePumpAsync()
        {
            Exception? error = null;
            try
            {
                while (true)
                {
                    await _items.WaitAsync().ConfigureAwait(false);
                    if (!IsOpen)
                    {
                        break; // 로컬 Close — 남은 큐잉분은 버린다(Tcp의 로컬 Close와 동일)
                    }

                    if (!_inbox.TryDequeue(out var packet))
                    {
                        continue;
                    }

                    if (ReferenceEquals(packet, ClosedByPeerSentinel))
                    {
                        break; // 상대 종료 — 드레인 완료
                    }

                    _slots.Release();
                    _handler.OnPacket(this, packet); // 배열 소유권 이전 — 이후 이 배열을 건드리지 않는다
                }
            }
            catch (Exception ex)
            {
                error = ex; // OnPacket이 던짐 — Tcp 수신 루프와 동일하게 error로 종료
            }
            finally
            {
                Close(); // 상대에게 종료 통지 포함(이미 닫혔으면 멱등 no-op)
                _handler.OnClosed(this, error);
            }
        }
    }
}
