namespace Bun3.Server.Hosting;

/// <summary>Server hosting options bound from the "Bun3:Server" configuration section.</summary>
public sealed class ServerOptions
{
    /// <summary>Section name used for configuration binding.</summary>
    public const string SectionName = "Bun3:Server";

    /// <summary>Listen port. 0 means an arbitrary port (for tests).</summary>
    public int Port { get; set; } = 20000;

    /// <summary>Bind address (IP string, e.g. "127.0.0.1"). Null/empty means all interfaces (Any).</summary>
    public string? BindAddress { get; set; }

    /// <summary>Maximum concurrent connections. Excess connections are closed immediately on accept. Zero or less = unlimited.</summary>
    public int MaxConnections { get; set; } = 1000;

    /// <summary>Maximum inbound packet size. Exceeding it closes the connection as a protocol violation.</summary>
    public int MaxPacketSize { get; set; } = 1024 * 1024;

    /// <summary>Maximum unprocessed packets queued per session. Exceeding it kicks the session.</summary>
    public int MaxQueuedPacketsPerSession { get; set; } = 256;

    /// <summary>TCP accept backlog.</summary>
    public int Backlog { get; set; } = 512;

    /// <summary>How long shutdown waits for session consume loops to finish.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Log a warning when a session queue item (handler or posted work) exceeds this duration. Zero or less = off.</summary>
    public TimeSpan SlowWorkWarning { get; set; } = TimeSpan.FromSeconds(1);
}
