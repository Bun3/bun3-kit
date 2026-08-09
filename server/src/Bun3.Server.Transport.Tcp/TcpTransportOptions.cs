using System.Net;

namespace Bun3.Server.Transport.Tcp
{
    /// <summary>TcpTransportListener 구성 옵션.</summary>
    public sealed class TcpTransportOptions
    {
        /// <summary>리슨 포트. 0이면 임의 포트에 바인딩된다(BoundPort로 확인).</summary>
        public int Port { get; set; }

        /// <summary>바인드 주소. null이면 모든 인터페이스(Any). 로컬 전용 서버는
        /// <see cref="IPAddress.Loopback"/>을 지정한다.</summary>
        public IPAddress? BindAddress { get; set; }

        /// <summary>동시 연결 수 상한. 초과 연결은 수락 즉시 닫힌다. 0 이하 = 무제한.
        /// 미인증 연결 폭주로 인한 자원 고갈(세션당 수신 큐 메모리)을 상한으로 막는다.</summary>
        public int MaxConnections { get; set; } = 1000;

        /// <summary>수신 패킷 크기 상한. 초과 시 프로토콜 위반으로 연결을 종료한다.</summary>
        public int MaxPacketSize { get; set; } = 1024 * 1024;

        /// <summary>수락 대기 큐 크기(TcpListener.Start의 backlog).</summary>
        public int Backlog { get; set; } = 512;
    }
}
