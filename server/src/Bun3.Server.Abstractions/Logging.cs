using System;

namespace Bun3.Server.Abstractions
{
    public enum Bun3LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    /// <summary>최소 로깅 계약. 호스팅 계층에서 Microsoft.Extensions.Logging으로 브리지된다.</summary>
    public interface IBun3Logger
    {
        void Log(Bun3LogLevel level, string message, Exception? exception = null);
    }

    public sealed class NullBun3Logger : IBun3Logger
    {
        public static readonly NullBun3Logger Instance = new NullBun3Logger();

        private NullBun3Logger() { }

        public void Log(Bun3LogLevel level, string message, Exception? exception = null) { }
    }
}
