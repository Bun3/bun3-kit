namespace Bun3.Server.Hosting;

/// <summary>구성 섹션 "Bun3:Server"에서 바인딩되는 서버 호스팅 옵션.</summary>
public sealed class ServerOptions
{
    /// <summary>구성 바인딩에 사용되는 섹션 이름.</summary>
    public const string SectionName = "Bun3:Server";

    /// <summary>리슨 포트. 0이면 임의 포트(테스트용).</summary>
    public int Port { get; set; } = 20000;

    /// <summary>바인드 주소(IP 문자열, 예: "127.0.0.1"). null/빈 값이면 모든 인터페이스(Any).</summary>
    public string? BindAddress { get; set; }

    /// <summary>동시 연결 수 상한. 초과 연결은 수락 즉시 닫힌다. 0 이하 = 무제한.</summary>
    public int MaxConnections { get; set; } = 1000;

    /// <summary>수신 패킷 크기 상한. 초과 시 프로토콜 위반으로 연결을 종료한다.</summary>
    public int MaxPacketSize { get; set; } = 1024 * 1024;

    /// <summary>세션당 큐에 쌓을 수 있는 미처리 패킷 수 상한. 초과 시 세션을 kick한다.</summary>
    public int MaxQueuedPacketsPerSession { get; set; } = 256;

    /// <summary>TCP accept 백로그.</summary>
    public int Backlog { get; set; } = 512;

    /// <summary>종료 시 세션 소비 루프 종료를 기다리는 시간.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>세션 큐 항목(핸들러·Post 작업)이 이 시간을 넘기면 경고 로그. 0 이하 = 끔.</summary>
    public TimeSpan SlowWorkWarning { get; set; } = TimeSpan.FromSeconds(1);
}
