namespace CactusNeedleSharp;

public sealed record NeedleWorkerPoolOptions
{
    public required string WorkerPath { get; init; }
    public IReadOnlyList<string> WorkerArguments { get; init; } = Array.Empty<string>();
    public int MaximumWorkers { get; init; } = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public NeedleOptions Runtime { get; init; } = new();
}

public interface INeedleWorkerPool : IAsyncDisposable
{
    int MaximumWorkers { get; }
    int WorkerCount { get; }
    ValueTask<INeedleSession> CreateSessionAsync(
        IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class NeedleWorkerException : NeedleException
{
    public NeedleWorkerException(string message, Exception? inner = null) : base(message, inner) { }
}
