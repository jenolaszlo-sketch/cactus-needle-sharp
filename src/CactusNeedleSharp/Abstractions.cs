using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CactusNeedleSharp;

/// <summary>Compiles natural-language input into tool calls against a set of tools.</summary>
public interface IToolCallCompiler
{
    /// <summary>Compiles <paramref name="input"/> into zero or more tool calls using <paramref name="tools"/>.</summary>
    ValueTask<ToolCallCompilation> CompileAsync(string input, IReadOnlyList<NeedleTool> tools,
        NeedleCompilationOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// The backend-neutral planning seam: depend on this instead of
/// <see cref="NeedleClient"/> when orchestration must stay portable across
/// tool-calling backends. It intentionally mirrors
/// <see cref="IToolCallCompiler"/> today; backend-specific planning
/// operations will extend this interface, not the compiler one.
/// </summary>
public interface IToolCallPlanner : IToolCallCompiler;

/// <summary>Creates <see cref="NeedleClient"/> instances.</summary>
public interface INeedleClientFactory
{
    /// <summary>Creates and initializes a <see cref="NeedleClient"/>.</summary>
    ValueTask<NeedleClient> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Extracts strongly-typed values from natural-language input.</summary>
public interface IStructuredExtractor
{
    /// <summary>Extracts a value of type <typeparamref name="T"/> from <paramref name="input"/>.</summary>
    [RequiresUnreferencedCode("Extraction reflects over the result type. Use the JsonTypeInfo overload for trimmed hosts.")]
    [RequiresDynamicCode("Extraction reflects over the result type. Use the JsonTypeInfo overload for NativeAOT hosts.")]
    ValueTask<NeedleExtractionResult<T>> ExtractAsync<T>(string input,
        NeedleExtractionOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Represents one live in-process session holding the exclusive native runtime lease.</summary>
public interface INeedleSession : IAsyncDisposable
{
    /// <summary>Gets the unique identifier for this session.</summary>
    string SessionId { get; }
    /// <summary>Gets the tools available to this session.</summary>
    IReadOnlyList<NeedleTool> Tools { get; }
    /// <summary>Runs inference for <paramref name="input"/> and returns the compiled tool calls.</summary>
    ValueTask<ToolCallCompilation> CompleteAsync(string input, NeedleCompilationOptions? options = null,
        CancellationToken cancellationToken = default);
    /// <summary>Resets the native conversation state for this session.</summary>
    ValueTask ResetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates <see cref="INeedleSession"/> instances.</summary>
public interface INeedleSessionFactory
{
    /// <summary>Creates a session over <paramref name="tools"/> and acquires the runtime lease.</summary>
    ValueTask<INeedleSession> CreateSessionAsync(IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Obsolete shim that forwards to <see cref="CreateSessionAsync"/>.</summary>
    [Obsolete("Use CreateSessionAsync, which states what is created.")]
    ValueTask<INeedleSession> CreateAsync(IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null, CancellationToken cancellationToken = default) =>
        CreateSessionAsync(tools, options, cancellationToken);
}

/// <summary>Resolves the native Needle runtime artifacts.</summary>
public interface INeedleArtifactProvider
{
    /// <summary>Returns the resolved native library path, version, and source.</summary>
    ValueTask<NeedleArtifacts> GetArtifactsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Represents the result of compiling input into tool calls.</summary>
public sealed record ToolCallCompilation
{
    /// <summary>Gets whether compilation succeeded.</summary>
    public required bool Success { get; init; }
    /// <summary>Gets the compiled tool calls, empty when there is no call.</summary>
    public IReadOnlyList<NeedleToolCall> Calls { get; init; } = Array.Empty<NeedleToolCall>();
    /// <summary>Gets the model-reported confidence, if any.</summary>
    public double? Confidence { get; init; }
    /// <summary>Gets model reasoning text, if any.</summary>
    public string? Reasoning { get; init; }
    /// <summary>Gets the failure message when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the machine-readable failure code, if any.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>Gets prefill throughput in tokens per second, if reported.</summary>
    public double? PrefillTokensPerSecond { get; init; }
    /// <summary>Gets decode throughput in tokens per second, if reported.</summary>
    public double? DecodeTokensPerSecond { get; init; }
    /// <summary>Returns true when compilation succeeded with at least one call meeting <paramref name="threshold"/>.</summary>
    public bool IsConfident(double threshold) => Success && Calls.Count > 0 && Confidence >= threshold;
    /// <summary>Maps this result to a <see cref="NeedleCompilationOutcome"/> using <paramref name="policy"/>.</summary>
    public NeedleCompilationOutcome GetOutcome(NeedleConfidencePolicy? policy = null)
    {
        if (!Success) return NeedleCompilationOutcome.Failed;
        if (Calls.Count == 0) return NeedleCompilationOutcome.NoCall;
        var minimum = (policy ?? new()).MinimumConfidence;
        return Confidence is null || Confidence >= minimum
            ? NeedleCompilationOutcome.Success
            : NeedleCompilationOutcome.LowConfidence;
    }
}

/// <summary>Describes the outcome of a tool-call compilation.</summary>
public enum NeedleCompilationOutcome
{
    /// <summary>Compilation succeeded with sufficient confidence.</summary>
    Success,
    /// <summary>Compilation succeeded but produced no tool call.</summary>
    NoCall,
    /// <summary>Compilation succeeded but confidence fell below the required minimum.</summary>
    LowConfidence,
    /// <summary>Compilation failed.</summary>
    Failed
}

/// <summary>Represents a single planned tool invocation.</summary>
public sealed record NeedleToolCall
{
    /// <summary>Gets the name of the tool to invoke.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the JSON arguments for the call.</summary>
    public required JsonElement Arguments { get; init; }
}

/// <summary>Represents the result of extracting a strongly-typed value from input.</summary>
/// <typeparam name="T">The extracted value type.</typeparam>
public sealed record NeedleExtractionResult<T>
{
    /// <summary>Gets whether extraction succeeded.</summary>
    public required bool Success { get; init; }
    /// <summary>Gets the extracted value, if any.</summary>
    public T? Value { get; init; }
    /// <summary>Gets the model-reported confidence, if any.</summary>
    public double? Confidence { get; init; }
    /// <summary>Gets the failure message when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the underlying tool-call compilation.</summary>
    public ToolCallCompilation Compilation { get; init; } = new() { Success = false };
    /// <summary>Maps the underlying compilation to a <see cref="NeedleCompilationOutcome"/> using <paramref name="policy"/>.</summary>
    public NeedleCompilationOutcome GetOutcome(NeedleConfidencePolicy? policy = null) => Compilation.GetOutcome(policy);
}
