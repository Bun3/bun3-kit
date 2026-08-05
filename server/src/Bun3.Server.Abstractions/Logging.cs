using System;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Abstractions
{
    /// <summary>사용자 제공 로거의 예외가 프레임워크 루프를 죽이지 않도록 감싸는 래퍼.</summary>
    public sealed class SafeLogger : ILogger
    {
        private readonly ILogger _inner;

        /// <summary>내부 로거를 감싸는 SafeLogger를 생성한다.</summary>
        public SafeLogger(ILogger inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>내부 로거의 BeginScope를 호출한다. 예외 발생 시 null을 반환한다.</summary>
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

        /// <summary>내부 로거의 IsEnabled를 호출한다. 예외 발생 시 false를 반환한다.</summary>
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

        /// <summary>내부 로거에 로그를 기록한다. 예외가 발생해도 상위로 전파하지 않는다.</summary>
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
