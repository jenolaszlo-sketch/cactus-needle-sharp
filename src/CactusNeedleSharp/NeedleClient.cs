using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CactusNeedleSharp;

public sealed class NeedleClient : IToolCallCompiler, IToolCallPlanner, INeedleSessionFactory, IStructuredExtractor, IAsyncDisposable
{
    private static readonly SemaphoreSlim RuntimeLease = new(1, 1);
    private static string? LoadedWeightsPath;
    private readonly NeedleOptions _options;
    private readonly INeedleArtifactProvider _artifacts;
    private readonly ILogger _logger;
    private bool _disposed;

    public NeedleRuntimeInfo RuntimeInfo { get; }

    private NeedleClient(NeedleOptions options, INeedleArtifactProvider artifacts, NeedleArtifacts resolved, ILogger logger)
    {
        _options = options; _artifacts = artifacts; _logger = logger;
        NeedleNative.Load(resolved.NativeLibraryPath);
        RuntimeInfo = new() { WrapperVersion = typeof(NeedleClient).Assembly.GetName().Version?.ToString(), RuntimeVersion = resolved.Version, ModelVersion = "needle2", ModelSource = resolved.Source };
    }

    public static async ValueTask<NeedleClient> CreateAsync(NeedleOptions? options = null,
        INeedleArtifactProvider? artifactProvider = null, ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        Validate(options);
        artifactProvider ??= new HuggingFaceNeedleArtifactProvider(options);
        var artifacts = await artifactProvider.GetArtifactsAsync(cancellationToken).ConfigureAwait(false);
        return new(options, artifactProvider, artifacts, (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<NeedleClient>());
    }

    public async ValueTask<ToolCallCompilation> CompileAsync(string input, IReadOnlyList<NeedleTool> tools,
        NeedleCompilationOptions? options = null, CancellationToken cancellationToken = default)
    {
        await using var session = await CreateAsync(tools, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await session.CompleteAsync(input, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<INeedleSession> CreateAsync(IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(tools);
        if (tools.Count == 0) throw new NeedleSchemaException("At least one tool is required.");
        await RuntimeLease.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var resolved = await _artifacts.GetArtifactsAsync(cancellationToken).ConfigureAwait(false);
            NeedleNative.Load(resolved.NativeLibraryPath);
            var weights = options?.WeightsPath ?? _options.ModelPath;
            var customWeights = !string.IsNullOrWhiteSpace(weights);
            if (customWeights)
            {
                var fullWeightsPath = Path.GetFullPath(weights!);
                if (!string.Equals(LoadedWeightsPath, fullWeightsPath, StringComparison.OrdinalIgnoreCase))
                {
                    LoadWeights(fullWeightsPath);
                    LoadedWeightsPath = fullWeightsPath;
                }
            }
            else if (LoadedWeightsPath is not null)
            {
                throw new NeedleInitializationException($"Custom weights '{LoadedWeightsPath}' are already loaded and the native runtime cannot return to base weights. Use a separate worker process for base-model sessions.");
            }
            var facts = options?.SystemFacts ?? options?.Facts?.ToString();
            var result = NeedleNative.Init(facts, NeedleProtocol.SerializeTools(tools), options?.ToolIndexPath ?? _options.ToolIndexPath);
            if (result < 0) throw new NeedleInitializationException($"needle_init failed with code {result}.");
            _logger.LogInformation("Needle session created with {ToolCount} tools.", tools.Count);
            return new NeedleSession(tools.ToArray(), _options, _logger, RuntimeLease, customWeights);
        }
        catch { RuntimeLease.Release(); throw; }
    }

    public async ValueTask<NeedleExtractionResult<T>> ExtractAsync<T>(string input,
        NeedleExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var tool = NeedleTool.FromType<T>("extract", options?.Description ?? $"Extract a {typeof(T).Name} record from text");
        var compilation = await CompileAsync(input, [tool], new() { MaxNewTokens = options?.MaxNewTokens }, cancellationToken).ConfigureAwait(false);
        T? value = default;
        if (compilation.Success && compilation.Calls.Count != 0)
        {
            try { value = compilation.Calls[0].Arguments.Deserialize<T>(NeedleProtocol.Json); }
            catch (JsonException exception) { throw new NeedleProtocolException($"Needle output could not be deserialized as {typeof(T).Name}.", exception); }
        }
        return new() { Success = compilation.Success, Value = value, Confidence = compilation.Confidence, Error = compilation.Error, Compilation = compilation };
    }

    private static unsafe void LoadWeights(string path)
    {
        if (!File.Exists(path)) throw new NeedleArtifactNotFoundException($"Custom Needle weights were not found at '{path}'.");
        var bytes = File.ReadAllBytes(path);
        fixed (byte* pointer = bytes)
        { var code = NeedleNative.LoadWeights(pointer, (ulong)bytes.LongLength); if (code != 0) throw new NeedleInitializationException($"needle_load failed with code {code}."); }
    }

    private static void Validate(NeedleOptions options)
    {
        if (options.ResponseBufferSize < 1024) throw new ArgumentOutOfRangeException(nameof(options.ResponseBufferSize));
        if (options.DefaultMaxNewTokens <= 0) throw new ArgumentOutOfRangeException(nameof(options.DefaultMaxNewTokens));
    }

    public ValueTask DisposeAsync() { _disposed = true; return ValueTask.CompletedTask; }
}

internal sealed class NeedleSession : INeedleSession
{
    private readonly NeedleOptions _defaults;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lease;
    private readonly SemaphoreSlim _flight = new(1, 1);
    private readonly bool _customWeights;
    private bool _disposed;
    public IReadOnlyList<NeedleTool> Tools { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    internal NeedleSession(IReadOnlyList<NeedleTool> tools, NeedleOptions defaults, ILogger logger, SemaphoreSlim lease, bool customWeights)
    { Tools = tools; _defaults = defaults; _logger = logger; _lease = lease; _customWeights = customWeights; }

    public async ValueTask<ToolCallCompilation> CompleteAsync(string input, NeedleCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        await _flight.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        using var activity = NeedleDiagnostics.Activities.StartActivity("needle.inference");
        activity?.SetTag("needle.session.id", SessionId);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buffer = new byte[_defaults.ResponseBufferSize];
            var returnCode = NeedleNative.Complete(input, options?.MaxNewTokens ?? _defaults.DefaultMaxNewTokens, buffer, buffer.Length);
            cancellationToken.ThrowIfCancellationRequested();
            if (returnCode < 0) throw new NeedleInferenceException($"needle_complete failed with code {returnCode}.");
            var length = Array.IndexOf(buffer, (byte)0);
            if (length <= 0) throw new NeedleProtocolException("Needle returned an empty or unterminated response. Increase ResponseBufferSize if necessary.");
            var result = NeedleProtocol.Parse(buffer.AsSpan(0, length));
            if (_customWeights) result = result with { Confidence = null };
            stopwatch.Stop();
            Record(result, stopwatch.Elapsed);
            _logger.LogInformation("Needle inference completed in {DurationMs}ms with {CallCount} calls and confidence {Confidence}.", stopwatch.Elapsed.TotalMilliseconds, result.Calls.Count, result.Confidence);
            return result;
        }
        finally { _flight.Release(); }
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _flight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { cancellationToken.ThrowIfCancellationRequested(); NeedleNative.Reset(); _logger.LogInformation("Needle session reset."); }
        finally { _flight.Release(); }
    }

    private static void Record(ToolCallCompilation result, TimeSpan duration)
    {
        NeedleDiagnostics.Duration.Record(duration.TotalMilliseconds); NeedleDiagnostics.Calls.Record(result.Calls.Count);
        if (result.Confidence is { } confidence) NeedleDiagnostics.Confidence.Record(confidence);
        if (result.PrefillTokensPerSecond is { } prefill) NeedleDiagnostics.Prefill.Record(prefill);
        if (result.DecodeTokensPerSecond is { } decode) NeedleDiagnostics.Decode.Record(decode);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed) { _disposed = true; _flight.Dispose(); _lease.Release(); }
        return ValueTask.CompletedTask;
    }
}
