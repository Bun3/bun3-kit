using Bun3.Server.Players;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Hosting;

internal sealed class PlayerCommandWorker<TPlayer> : IHostedService, IDisposable where TPlayer : Player
{
    private readonly PlayerCommandQueue _queue;
    private readonly PlayerCommandDispatcher<TPlayer> _dispatcher;
    private readonly PlayerRegistry<TPlayer> _registry;
    private readonly ILogger _logger;
    private readonly bool _standalone;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;
    private readonly int _concurrency;
    private readonly IReadOnlyList<(string Kind, int InputSchemaVersion)> _supportedInputs;
    private readonly CancellationTokenSource _stop = new();
    private Task _running = Task.CompletedTask;
    private bool _started;
    internal PlayerCommandWorker(PlayerCommandQueue queue, PlayerCommandCatalog<TPlayer> catalog, PlayerRegistry<TPlayer> registry,
        PlayerCommandWorkerOptions options, bool standalone, ILogger logger)
    {
        if (options.PollInterval <= TimeSpan.Zero || options.BatchSize < 1 || options.MaxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Worker options must be positive.");
        if (registry.WriteBinding?.Role != (standalone ? PlayerWriteRole.OfflineAdministrator : PlayerWriteRole.Server))
            throw new InvalidOperationException("Command worker role differs from the player store binding.");
        _queue = queue; _registry = registry; _logger = logger; _standalone = standalone;
        _dispatcher = new(queue, catalog, registry); _interval = options.PollInterval; _batchSize = options.BatchSize; _concurrency = options.MaxConcurrency;
        _supportedInputs = catalog.GetSupportedInputs(standalone);
    }
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_started) throw new InvalidOperationException("A command worker cannot be restarted.");
        _started = true; _running = RunAsync(_stop.Token); return Task.CompletedTask;
    }
    internal void StopIntake() => _stop.Cancel();
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        StopIntake(); await _running.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_standalone)
        {
            var drain = await _registry.DrainAsync(cancellationToken).ConfigureAwait(false);
            if (!drain.IsQuiescent) throw new InvalidOperationException("Offline command executor did not finish cleanup.");
        }
    }
    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var candidates = await _queue.FindPendingAsync(_registry.WriteAuthority!.ScopeKey, _batchSize, _supportedInputs, ct).ConfigureAwait(false);
                var next = -1;
                async Task ExecuteCandidates()
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var index = Interlocked.Increment(ref next); if (index >= candidates.Count) return;
                        try { await _dispatcher.ExecuteAsync(candidates[index], CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception ex) { LogFailure(ex, candidates[index]); }
                    }
                }
                var workers = new Task[Math.Min(_concurrency, candidates.Count)];
                for (var i = 0; i < workers.Length; i++) workers[i] = ExecuteCandidates();
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { LogFailure(ex, null); }
            try { await Task.Delay(_interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
    private void LogFailure(Exception error, Guid? id)
    {
        try { _logger.LogWarning("Command worker failed scope {ScopeKey} command {CommandId} error {ErrorType}",
            _registry.WriteAuthority!.ScopeKey, id, error.GetType().FullName); } catch { }
    }
    public void Dispose() => StopIntake();
}
