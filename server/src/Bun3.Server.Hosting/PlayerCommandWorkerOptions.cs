namespace Bun3.Server.Hosting;

/// <summary>Controls durable command polling and bounded execution concurrency.</summary>
public sealed class PlayerCommandWorkerOptions
{
    /// <summary>Gets or sets the positive polling interval. Defaults to one second.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Gets or sets the positive maximum candidate count per poll. Defaults to 32.</summary>
    public int BatchSize { get; set; } = 32;
    /// <summary>Gets or sets the positive maximum concurrent executions. Defaults to four.</summary>
    public int MaxConcurrency { get; set; } = 4;
}
