namespace Bun3.Server.Transport.Tcp
{
    /// <summary>TcpConnector 구성 옵션.</summary>
    public sealed class TcpConnectorOptions
    {
        /// <summary>접속할 호스트명 또는 IP.</summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>접속할 포트.</summary>
        public int Port { get; set; }

        /// <summary>수신 패킷 크기 상한. 초과 시 프로토콜 위반으로 연결을 종료한다.</summary>
        public int MaxPacketSize { get; set; } = 1024 * 1024;
    }
}
