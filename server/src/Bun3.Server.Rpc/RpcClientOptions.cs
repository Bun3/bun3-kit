using System;

namespace Bun3.Server.Rpc
{
    /// <summary>Options tuning RpcClient behavior.</summary>
    public sealed class RpcClientOptions
    {
        /// <summary>Per-request response deadline. On expiry only that request fails with TimeoutException.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Ping interval. null = disabled.</summary>
        public TimeSpan? PingInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>When true, push callbacks and the Closed event are posted to the SynchronizationContext captured at connect time (Unity main thread).</summary>
        public bool UseSynchronizationContext { get; set; } = true;
    }
}
