namespace Bun3.Server.Hosting;

/// <summary>구성 섹션 "Bun3:Server"에서 바인딩되는 서버 호스팅 옵션.</summary>
public sealed class ServerOptions
{
    public const string SectionName = "Bun3:Server";

    /// <summary>리슨 포트. 0이면 임의 포트(테스트용).</summary>
    public int Port { get; set; } = 20000;

    public int MaxPacketSize { get; set; } = 1024 * 1024;

    public int MaxQueuedPacketsPerSession { get; set; } = 256;

    /// <summary>TCP accept 백로그.</summary>
    public int Backlog { get; set; } = 512;

    /// <summary>종료 시 세션 소비 루프 종료를 기다리는 시간.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
