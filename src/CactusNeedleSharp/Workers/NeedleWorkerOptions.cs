namespace CactusNeedleSharp;

/// <summary>Configures the out-of-process worker pool.</summary>
public sealed record NeedleWorkerPoolOptions
{
    /// <summary>Gets the worker executable path, resolved by <see cref="NeedleWorkerLocator"/> by default.</summary>
    public string WorkerPath { get; init; } = NeedleWorkerLocator.ResolvePath();
    /// <summary>Gets extra arguments passed to each worker process.</summary>
    public IReadOnlyList<string> WorkerArguments { get; init; } = Array.Empty<string>();
    /// <summary>Gets the maximum number of live worker processes.</summary>
    public int MaximumWorkers { get; init; } = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
    /// <summary>Gets how long to wait for a worker to start.</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets how long a single worker request may take.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Gets how long to wait for graceful worker shutdown.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets how long a session request waits for a worker lease; null waits indefinitely. Defaults to 10 seconds.</summary>
    public TimeSpan? QueueTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets how long an idle worker is retained before disposal.</summary>
    public TimeSpan? IdleWorkerTimeout { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Gets the maximum number of session requests waiting for a worker.</summary>
    public int MaximumQueueLength { get; init; } = 100;
    /// <summary>Gets the maximum worker-protocol message length in characters.</summary>
    public int MaximumProtocolMessageLength { get; init; } = 1024 * 1024;
    /// <summary>Gets optional admission control invoked before starting a new worker.</summary>
    public Func<NeedleWorkerAdmissionContext, CancellationToken, ValueTask<bool>>? AdmissionCheck { get; init; }
    /// <summary>Gets the runtime options forwarded to each worker.</summary>
    public NeedleOptions Runtime { get; init; } = new();
}

/// <summary>Describes pool load supplied to the worker admission check.</summary>
/// <param name="ActiveWorkers">The number of live worker processes.</param>
/// <param name="IdleWorkers">The number of idle reusable workers.</param>
/// <param name="WaitingSessions">The number of session requests waiting for a worker.</param>
/// <param name="MaximumWorkers">The maximum number of live workers.</param>
public sealed record NeedleWorkerAdmissionContext(int ActiveWorkers, int IdleWorkers, int WaitingSessions, int MaximumWorkers);

/// <summary>Locates the worker executable.</summary>
public static class NeedleWorkerLocator
{
    /// <summary>Resolves the worker executable path from the app directory, falling back to the bare file name.</summary>
    public static string ResolvePath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var fileName = OperatingSystem.IsWindows() ? "CactusNeedleSharp.Worker.exe" : "CactusNeedleSharp.Worker";
        var executable = Path.Combine(baseDirectory, fileName);
        if (File.Exists(executable)) return executable;
        var assembly = Path.Combine(baseDirectory, "CactusNeedleSharp.Worker.dll");
        return File.Exists(assembly) ? assembly : fileName;
    }
}

/// <summary>Manages a pool of out-of-process worker sessions.</summary>
public interface INeedleWorkerPool : IAsyncDisposable
{
    /// <summary>Gets the maximum number of live worker processes.</summary>
    int MaximumWorkers { get; }
    /// <summary>Gets the current number of live worker processes.</summary>
    int WorkerCount { get; }
    /// <summary>Gets the number of idle reusable workers.</summary>
    int IdleWorkerCount { get; }
    /// <summary>Gets the number of session requests waiting for a worker.</summary>
    int WaitingSessionCount { get; }
    /// <summary>Starts workers so at least <paramref name="workerCount"/> are ready.</summary>
    ValueTask WarmAsync(int workerCount, CancellationToken cancellationToken = default);
    /// <summary>Creates a session on a pooled worker, waiting up to the configured queue timeout for capacity.</summary>
    ValueTask<INeedleSession> CreateSessionAsync(
        IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when worker creation, queuing, or admission control fails.</summary>
public sealed class NeedleWorkerException : NeedleException
{
    public NeedleWorkerException(string message, Exception? inner = null) : base(message, inner) { }
}
