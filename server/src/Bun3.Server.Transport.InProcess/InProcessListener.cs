using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Transport.InProcess
{
    /// <summary>In-process listener. There is no socket, so it only registers the handler — no accept loop.</summary>
    internal sealed class InProcessListener : ITransportListener
    {
        private volatile IConnectionHandler? _handler;
        private volatile bool _stopped;
        private int _started;

        /// <remarks>Single use: cannot be restarted after StopAsync (same as the TCP listener).</remarks>
        public Task StartAsync(IConnectionHandler handler, CancellationToken ct = default)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                throw new InvalidOperationException("Listener is already started.");
            }

            _handler = handler;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _stopped = true; // Only stops new acceptance — closing existing connections is the caller's responsibility (contract).
            return Task.CompletedTask;
        }

        /// <summary>The server handler if accepting; null if not started or stopped.</summary>
        internal IConnectionHandler? TryGetAcceptHandler() => _stopped ? null : _handler;
    }
}
