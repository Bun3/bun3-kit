using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Hosting;

internal sealed class LoggerBridge : IServerLogger
{
    private readonly ILogger _logger;

    public LoggerBridge(ILogger logger) => _logger = logger;

    public void Log(ServerLogLevel level, string message, Exception? exception = null) =>
        _logger.Log(Map(level), exception, "{Message}", message);

    private static LogLevel Map(ServerLogLevel level) => level switch
    {
        ServerLogLevel.Debug => LogLevel.Debug,
        ServerLogLevel.Info => LogLevel.Information,
        ServerLogLevel.Warning => LogLevel.Warning,
        _ => LogLevel.Error,
    };
}
