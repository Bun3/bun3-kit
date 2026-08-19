using System;

namespace Bun3.Server.Rpc
{
    /// <summary>Delivered to pending awaits when the connection closes before a request can complete.</summary>
    public sealed class ConnectionClosedException : Exception
    {
        /// <summary>Creates the exception with the given message.</summary>
        public ConnectionClosedException(string message) : base(message) { }
    }
}
