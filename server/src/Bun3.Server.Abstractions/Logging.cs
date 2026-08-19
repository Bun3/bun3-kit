using System;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Abstractions
{
    /// <summary>Wrapper that keeps exceptions from a user-supplied logger from killing framework loops.</summary>
    public sealed class SafeLogger : ILogger
    {
        private readonly ILogger _inner;

        /// <summary>Creates a SafeLogger wrapping the given inner logger.</summary>
        public SafeLogger(ILogger inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>Calls the inner logger's BeginScope. Returns null if it throws.</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            try
            {
                return _inner.BeginScope(state);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Calls the inner logger's IsEnabled. Returns false if it throws.</summary>
        public bool IsEnabled(LogLevel logLevel)
        {
            try
            {
                return _inner.IsEnabled(logLevel);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Logs to the inner logger. Exceptions are swallowed, never propagated.</summary>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
            }
            catch
            {
                // Logging failures must not affect server behavior.
            }
        }
    }
}
