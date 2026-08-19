namespace Bun3.Server.Rpc
{
    /// <summary>Channel value in the first byte of a packet. 0x10 and above are reserved (game custom / high-frequency channels).</summary>
    public static class Channels
    {
        /// <summary>Framework-owned control messages (Ping/Pong etc.).</summary>
        public const byte Control = 0x01;

        /// <summary>Client-to-server request.</summary>
        public const byte Request = 0x02;

        /// <summary>Server-to-client response to a request.</summary>
        public const byte Response = 0x03;

        /// <summary>Server-to-client unsolicited push.</summary>
        public const byte Update = 0x04;
    }
}
