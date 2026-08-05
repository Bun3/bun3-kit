using System;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Abstractions
{
    /// <summary>사용자 제공 로거의 예외가 프레임워크 루프를 죽이지 않도록 감싸는 래퍼.</summary>
    public sealed class SafeLogger : ILogger
    {
        private readonly ILogger _inner;

        public SafeLogger(ILogger inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

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
                // 로깅 실패가 서버 동작을 해치면 안 된다
            }
        }
    }
}
