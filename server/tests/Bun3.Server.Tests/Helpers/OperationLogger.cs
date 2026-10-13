using Microsoft.Extensions.Logging;
namespace Bun3.Server.Tests.Helpers;
internal sealed class OperationLogger : ILogger
{
    internal bool Throws;
    internal readonly List<Dictionary<string, object?>> Entries = new();
    internal readonly List<Exception?> Errors = new();
    internal IEnumerable<string> Stages => Entries.Where(e => e.ContainsKey("Stage")).Select(e => (string)e["Stage"]!);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? error, Func<TState, Exception?, string> formatter)
    {
        if (Throws) throw new InvalidOperationException("sink");
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
            lock (Entries) { Entries.Add(values.ToDictionary(v => v.Key, v => v.Value)); Errors.Add(error); }
    }
}
