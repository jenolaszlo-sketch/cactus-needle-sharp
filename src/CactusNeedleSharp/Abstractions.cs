using System.Text.Json;

namespace CactusNeedleSharp;

public interface IToolCallCompiler
{
    ValueTask<ToolCallCompilation> CompileAsync(string input, IReadOnlyList<NeedleTool> tools,
        NeedleCompilationOptions? options = null, CancellationToken cancellationToken = default);
}

public interface IStructuredExtractor
{
    ValueTask<NeedleExtractionResult<T>> ExtractAsync<T>(string input,
        NeedleExtractionOptions? options = null, CancellationToken cancellationToken = default);
}

public interface INeedleSession : IAsyncDisposable
{
    IReadOnlyList<NeedleTool> Tools { get; }
    ValueTask<ToolCallCompilation> CompleteAsync(string input, NeedleCompilationOptions? options = null,
        CancellationToken cancellationToken = default);
    ValueTask ResetAsync(CancellationToken cancellationToken = default);
}

public interface INeedleSessionFactory
{
    ValueTask<INeedleSession> CreateAsync(IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null, CancellationToken cancellationToken = default);
}

public interface INeedleArtifactProvider
{
    ValueTask<NeedleArtifacts> GetArtifactsAsync(CancellationToken cancellationToken = default);
}

public sealed record ToolCallCompilation
{
    public required bool Success { get; init; }
    public IReadOnlyList<NeedleToolCall> Calls { get; init; } = Array.Empty<NeedleToolCall>();
    public double? Confidence { get; init; }
    public string? Reasoning { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public double? PrefillTokensPerSecond { get; init; }
    public double? DecodeTokensPerSecond { get; init; }
    public bool IsConfident(double threshold) => Success && Calls.Count > 0 && Confidence is >= 0 && Confidence >= threshold;
}

public sealed record NeedleToolCall
{
    public required string Name { get; init; }
    public required JsonElement Arguments { get; init; }
}

public sealed record NeedleExtractionResult<T>
{
    public required bool Success { get; init; }
    public T? Value { get; init; }
    public double? Confidence { get; init; }
    public string? Error { get; init; }
    public ToolCallCompilation Compilation { get; init; } = new() { Success = false };
}
