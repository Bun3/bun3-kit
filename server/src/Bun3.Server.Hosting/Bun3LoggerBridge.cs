using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Hosting;

internal sealed class Bun3LoggerBridge : IBun3Logger
{
    private readonly ILogger _logger;

    public Bun3LoggerBridge(ILogger logger) => _logger = logger;

    public void Log(Bun3LogLevel level, string message, Exception? exception = null) =>
        _logger.Log(Map(level), exception, "{Message}", message);

    private static LogLevel Map(Bun3LogLevel level) => level switch
    {
        Bun3LogLevel.Debug => LogLevel.Debug,
        Bun3LogLevel.Info => LogLevel.Information,
        Bun3LogLevel.Warning => LogLevel.Warning,
        _ => LogLevel.Error,
    };
}
