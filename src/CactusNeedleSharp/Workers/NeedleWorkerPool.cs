using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CactusNeedleSharp;

/// <summary>Pools out-of-process workers, bounding live processes and queueing excess session requests.</summary>
public sealed class NeedleWorkerPool : INeedleWorkerPool
{
    private readonly NeedleWorkerPoolOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _leases;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly ConcurrentBag<NeedleWorkerProcess> _idle = [];
    private readonly ConcurrentDictionary<NeedleWorkerProcess, byte> _workers = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private int _disposed;
    private int _waiting;

    /// <summary>Gets the maximum number of live worker processes.</summary>
    public int MaximumWorkers => _options.MaximumWorkers;
    /// <summary>Gets the current number of live worker processes.</summary>
    public int WorkerCount => _workers.Count;
    /// <summary>Gets the number of idle reusable workers.</summary>
    public int IdleWorkerCount => _idle.Count;
    /// <summary>Gets the number of session requests waiting for a worker.</summary>
    public int WaitingSessionCount => Volatile.Read(ref _waiting);

    /// <summary>Initializes the pool and validates worker limits and timeouts.</summary>
    public NeedleWorkerPool(NeedleWorkerPoolOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaximumWorkers <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaximumWorkers));
        if (options.StartupTimeout <= TimeSpan.Zero || options.RequestTimeout <= TimeSpan.Zero || options.ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Worker timeouts must be positive.");
        if (options.QueueTimeout is { } queueTimeout && queueTimeout <= TimeSpan.Zero ||
            options.IdleWorkerTimeout is { } idleTimeout && idleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Optional worker timeouts must be positive.");
        if (options.MaximumQueueLength < 0) throw new ArgumentOutOfRangeException(nameof(options.MaximumQueueLength));
        if (options.MaximumProtocolMessageLength < 1024 || options.MaximumProtocolMessageLength > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(options.MaximumProtocolMessageLength));
        _options = options;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<NeedleWorkerPool>();
        _leases = new(options.MaximumWorkers, options.MaximumWorkers);
    }

    /// <summary>Starts workers so at least <paramref name="workerCount"/> are ready, without exceeding the maximum.</summary>
    public async ValueTask WarmAsync(int workerCount, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (workerCount < 0 || workerCount > MaximumWorkers) throw new ArgumentOutOfRangeException(nameof(workerCount));
        // Take-or-start decisions hold the start gate so a warming start and
        // a session-creating start cannot interleave past MaximumWorkers.
        while (_workers.Count < workerCount)
        {
            await _leases.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch { _leases.Release(); throw; }
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (_workers.Count >= workerCount) return;
                var worker = await NeedleWorkerProcess.StartAsync(_options, _logger, cancellationToken).ConfigureAwait(false);
                if (_workers.TryAdd(worker, 0))
                {
                    _idle.Add(worker);
                    NeedleDiagnostics.WorkersStarted.Add(1);
                }
                else await worker.DisposeAsync().ConfigureAwait(false);
            }
            finally { _startGate.Release(); _leases.Release(); }
        }
    }

    /// <summary>Creates a session on a pooled worker, reusing idle workers or queueing up to the configured limits.</summary>
    public async ValueTask<INeedleSession> CreateSessionAsync(IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(tools);
        if (tools.Count == 0) throw new NeedleSchemaException("At least one tool is required.");
        var queueStarted = Stopwatch.GetTimestamp();
        if (!_leases.Wait(0))
        {
            var queued = Interlocked.Increment(ref _waiting);
            if (queued > _options.MaximumQueueLength)
            {
                Interlocked.Decrement(ref _waiting);
                throw new NeedleWorkerException($"Needle worker queue limit of {_options.MaximumQueueLength} was reached.");
            }
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            if (_options.QueueTimeout is { } queueTimeout) waitCancellation.CancelAfter(queueTimeout);
            try { await _leases.WaitAsync(waitCancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            { throw new ObjectDisposedException(nameof(NeedleWorkerPool)); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _options.QueueTimeout is not null)
            { throw new TimeoutException($"Timed out waiting {_options.QueueTimeout} for a Needle worker."); }
            finally { Interlocked.Decrement(ref _waiting); }
        }
        NeedleDiagnostics.QueueDuration.Record(Stopwatch.GetElapsedTime(queueStarted).TotalMilliseconds);
        if (Volatile.Read(ref _disposed) != 0) { _leases.Release(); throw new ObjectDisposedException(nameof(NeedleWorkerPool)); }
        NeedleWorkerProcess? worker = null;
        try { await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch { _leases.Release(); throw; }
        try
        {
            while (_idle.TryTake(out var candidate))
            {
                var expired = _options.IdleWorkerTimeout is { } idleTimeout && DateTimeOffset.UtcNow - candidate.LastUsedAt >= idleTimeout;
                if (candidate.IsHealthy && !expired)
                {
                    worker = candidate;
                    NeedleDiagnostics.WorkersReused.Add(1);
                    break;
                }
                await RemoveAndDisposeAsync(candidate).ConfigureAwait(false);
            }
            if (worker is null)
            {
                if (_options.AdmissionCheck is { } admission &&
                    !await admission(new(_workers.Count, _idle.Count, WaitingSessionCount, MaximumWorkers), cancellationToken).ConfigureAwait(false))
                    throw new NeedleWorkerException("Needle worker creation was rejected by admission control.");
                worker = await NeedleWorkerProcess.StartAsync(_options, _logger, cancellationToken).ConfigureAwait(false);
                _workers.TryAdd(worker, 0);
                NeedleDiagnostics.WorkersStarted.Add(1);
            }
            return await StartSessionAsync(worker, tools, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (worker is not null) await RemoveAndDisposeAsync(worker).ConfigureAwait(false);
            _leases.Release();
            throw;
        }
        finally { _startGate.Release(); }
    }

    private async ValueTask<INeedleSession> StartSessionAsync(
        NeedleWorkerProcess worker,
        IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options,
        CancellationToken cancellationToken)
    {
        try
        {
            await worker.InitializeAsync(tools, options, cancellationToken).ConfigureAwait(false);
            var customWeights = !string.IsNullOrWhiteSpace(options?.WeightsPath ?? _options.Runtime.ModelPath);
            return new NeedleWorkerSession(this, worker, tools.ToArray(), customWeights);
        }
        catch
        {
            await RemoveAndDisposeAsync(worker).ConfigureAwait(false);
            _leases.Release();
            throw;
        }
    }

    internal async ValueTask ReturnAsync(NeedleWorkerProcess worker, bool reusable)
    {
        try
        {
            if (Volatile.Read(ref _disposed) == 0 && reusable && worker.IsHealthy)
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
    {
        _workers.TryRemove(worker, out _);
        NeedleDiagnostics.WorkersDiscarded.Add(1);
        await worker.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Shuts down all workers and releases pool resources.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        var workers = _workers.Keys.ToArray();
        _workers.Clear();
        foreach (var worker in workers) await worker.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}

internal sealed class NeedleWorkerSession : INeedleSession
{
    private readonly NeedleWorkerPool _pool;
    private readonly NeedleWorkerProcess _worker;
    private readonly bool _customWeights;
    private readonly SemaphoreSlim _flight = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private int _disposed;
    public IReadOnlyList<NeedleTool> Tools { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    internal NeedleWorkerSession(NeedleWorkerPool pool, NeedleWorkerProcess worker,
        IReadOnlyList<NeedleTool> tools, bool customWeights)
    { _pool = pool; _worker = worker; Tools = tools; _customWeights = customWeights; }

    public async ValueTask<ToolCallCompilation> CompleteAsync(string input, NeedleCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        await _flight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            using var activity = NeedleDiagnostics.Activities.StartActivity("needle.worker.inference");
            activity?.SetTag("needle.session.id", SessionId);
            activity?.SetTag("process.pid", _worker.ProcessId);
            return await _worker.CompleteAsync(input, options, cancellationToken).ConfigureAwait(false);
        }
        finally { _flight.Release(); }
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _flight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await _worker.ResetAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _flight.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _flight.WaitAsync().ConfigureAwait(false);
        try { await _pool.ReturnAsync(_worker, reusable: !_customWeights).ConfigureAwait(false); }
        finally { _flight.Release(); _flight.Dispose(); }
    }
}
