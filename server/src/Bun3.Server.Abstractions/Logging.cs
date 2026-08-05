using System;

namespace Bun3.Server.Abstractions
{
    public enum ServerLogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    /// <summary>최소 로깅 계약. 호스팅 계층에서 Microsoft.Extensions.Logging으로 브리지된다.</summary>
    public interface IServerLogger
    {
        void Log(ServerLogLevel level, string message, Exception? exception = null);
    }

    public sealed class NullServerLogger : IServerLogger
    {
        public static readonly NullServerLogger Instance = new NullServerLogger();

        private NullServerLogger() { }

        public void Log(ServerLogLevel level, string message, Exception? exception = null) { }
    }

    /// <summary>사용자 제공 로거의 예외가 프레임워크 루프를 죽이지 않도록 감싸는 래퍼.</summary>
    public sealed class SafeServerLogger : IServerLogger
    {
        private readonly IServerLogger _inner;

        public SafeServerLogger(IServerLogger inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Log(ServerLogLevel level, string message, Exception? exception = null)
        {
            try
            {
                _inner.Log(level, message, exception);
            }
            catch
            {
                // 로깅 실패가 서버 동작을 해치면 안 된다
            }
        }
    }
}
