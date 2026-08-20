using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CactusNeedleSharp;

public sealed class NeedleWorkerPool : INeedleWorkerPool
{
    private readonly NeedleWorkerPoolOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _leases;
    private readonly ConcurrentBag<NeedleWorkerProcess> _idle = [];
    private readonly ConcurrentDictionary<NeedleWorkerProcess, byte> _workers = [];
    private bool _disposed;

    public int MaximumWorkers => _options.MaximumWorkers;
    public int WorkerCount => _workers.Count;

    public NeedleWorkerPool(NeedleWorkerPoolOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaximumWorkers <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaximumWorkers));
        if (options.StartupTimeout <= TimeSpan.Zero || options.RequestTimeout <= TimeSpan.Zero || options.ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Worker timeouts must be positive.");
        _options = options;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<NeedleWorkerPool>();
        _leases = new(options.MaximumWorkers, options.MaximumWorkers);
    }

    public async ValueTask<INeedleSession> CreateSessionAsync(IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(tools);
        if (tools.Count == 0) throw new NeedleSchemaException("At least one tool is required.");
        await _leases.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_disposed)
        {
            _leases.Release();
            throw new ObjectDisposedException(nameof(NeedleWorkerPool));
        }
        NeedleWorkerProcess? worker = null;
        try
        {
            while (_idle.TryTake(out var candidate))
            {
                if (candidate.IsHealthy) { worker = candidate; break; }
                await RemoveAndDisposeAsync(candidate).ConfigureAwait(false);
            }
            if (worker is null)
            {
                worker = await NeedleWorkerProcess.StartAsync(_options, _logger, cancellationToken).ConfigureAwait(false);
                _workers.TryAdd(worker, 0);
            }
            await worker.InitializeAsync(tools, options, cancellationToken).ConfigureAwait(false);
            var customWeights = !string.IsNullOrWhiteSpace(options?.WeightsPath ?? _options.Runtime.ModelPath);
            return new NeedleWorkerSession(this, worker, tools.ToArray(), customWeights);
        }
        catch
        {
            if (worker is not null) await RemoveAndDisposeAsync(worker).ConfigureAwait(false);
            _leases.Release();
            throw;
        }
    }

    internal async ValueTask ReturnAsync(NeedleWorkerProcess worker, bool reusable)
    {
        try
        {
            if (!_disposed && reusable && worker.IsHealthy)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(_options.ShutdownTimeout);
                    await worker.CloseSessionAsync(timeout.Token).ConfigureAwait(false);
                    if (worker.IsHealthy) { _idle.Add(worker); return; }
                }
                catch (Exception exception) { _logger.LogWarning(exception, "Discarding unhealthy Needle worker {ProcessId}.", worker.ProcessId); }
            }
            await RemoveAndDisposeAsync(worker).ConfigureAwait(false);
        }
        finally { _leases.Release(); }
    }

    private async ValueTask RemoveAndDisposeAsync(NeedleWorkerProcess worker)
    { _workers.TryRemove(worker, out _); await worker.DisposeAsync().ConfigureAwait(false); }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var workers = _workers.Keys.ToArray();
        _workers.Clear();
        foreach (var worker in workers) await worker.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class NeedleWorkerSession : INeedleSession
{
    private readonly NeedleWorkerPool _pool;
    private readonly NeedleWorkerProcess _worker;
    private readonly bool _customWeights;
    private readonly SemaphoreSlim _flight = new(1, 1);
    private bool _disposed;
    public IReadOnlyList<NeedleTool> Tools { get; }

    internal NeedleWorkerSession(NeedleWorkerPool pool, NeedleWorkerProcess worker,
        IReadOnlyList<NeedleTool> tools, bool customWeights)
    { _pool = pool; _worker = worker; Tools = tools; _customWeights = customWeights; }

    public async ValueTask<ToolCallCompilation> CompleteAsync(string input, NeedleCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        await _flight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _worker.CompleteAsync(input, options, cancellationToken).ConfigureAwait(false); }
        finally { _flight.Release(); }
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _flight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _worker.ResetAsync(cancellationToken).ConfigureAwait(false); }
        finally { _flight.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _flight.Dispose();
        await _pool.ReturnAsync(_worker, reusable: !_customWeights).ConfigureAwait(false);
    }
}
