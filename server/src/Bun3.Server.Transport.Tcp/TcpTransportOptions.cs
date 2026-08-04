namespace Bun3.Server.Transport.Tcp
{
    public sealed class TcpTransportOptions
    {
        /// <summary>리슨 포트. 0이면 임의 포트에 바인딩된다(BoundPort로 확인).</summary>
        public int Port { get; set; }

        /// <summary>수신 프레임 크기 상한. 초과 시 프로토콜 위반으로 연결을 종료한다.</summary>
        public int MaxFrameSize { get; set; } = 1024 * 1024;

        public int Backlog { get; set; } = 512;
    }
}
