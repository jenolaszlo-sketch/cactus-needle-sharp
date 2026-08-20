namespace CactusNeedleSharp;

public sealed record NeedleWorkerPoolOptions
{
    public string WorkerPath { get; init; } = NeedleWorkerLocator.ResolvePath();
    public IReadOnlyList<string> WorkerArguments { get; init; } = Array.Empty<string>();
    public int MaximumWorkers { get; init; } = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan? QueueTimeout { get; init; }
    public TimeSpan? IdleWorkerTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public int MaximumQueueLength { get; init; } = 100;
    public int MaximumProtocolMessageLength { get; init; } = 1024 * 1024;
    public Func<NeedleWorkerAdmissionContext, CancellationToken, ValueTask<bool>>? AdmissionCheck { get; init; }
    public NeedleOptions Runtime { get; init; } = new();
}

public sealed record NeedleWorkerAdmissionContext(int ActiveWorkers, int IdleWorkers, int WaitingSessions, int MaximumWorkers);

public static class NeedleWorkerLocator
{
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

public interface INeedleWorkerPool : IAsyncDisposable
{
    int MaximumWorkers { get; }
    int WorkerCount { get; }
    int IdleWorkerCount { get; }
    int WaitingSessionCount { get; }
    ValueTask WarmAsync(int workerCount, CancellationToken cancellationToken = default);
    ValueTask<INeedleSession> CreateSessionAsync(
        IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class NeedleWorkerException : NeedleException
{
    public NeedleWorkerException(string message, Exception? inner = null) : base(message, inner) { }
}
