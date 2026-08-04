namespace Bun3.Server.Hosting;

/// <summary>구성 섹션 "Bun3:Server"에서 바인딩되는 서버 호스팅 옵션.</summary>
public sealed class Bun3ServerOptions
{
    public const string SectionName = "Bun3:Server";

    /// <summary>리슨 포트. 0이면 임의 포트(테스트용).</summary>
    public int Port { get; set; } = 20000;

    public int MaxFrameSize { get; set; } = 1024 * 1024;

    public int MaxQueuedFramesPerSession { get; set; } = 256;
}
