using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CactusNeedleSharp;

/// <summary>In-process Needle client; sessions share one live runtime lease and custom weights are a one-way door.</summary>
public sealed class NeedleClient : IToolCallCompiler, IToolCallPlanner, INeedleSessionFactory, IStructuredExtractor, IAsyncDisposable
{
    private static readonly SemaphoreSlim RuntimeLease = new(1, 1);
    private static string? LoadedWeightsPath;
    private readonly NeedleOptions _options;
    private readonly INeedleArtifactProvider _artifacts;
    private readonly bool _ownsArtifacts;
    private readonly ILogger _logger;
    private int _disposed;

    /// <summary>Gets wrapper, runtime, and model version information.</summary>
    public NeedleRuntimeInfo RuntimeInfo { get; }

    private NeedleClient(NeedleOptions options, INeedleArtifactProvider artifacts, bool ownsArtifacts,
        NeedleArtifacts resolved, ILogger logger)
    {
        _options = options; _artifacts = artifacts; _ownsArtifacts = ownsArtifacts; _logger = logger;
        NeedleNative.Load(resolved.NativeLibraryPath);
        RuntimeInfo = new() { WrapperVersion = typeof(NeedleClient).Assembly.GetName().Version?.ToString(), RuntimeVersion = resolved.Version, ModelVersion = "needle2", ModelSource = resolved.Source };
    }

    /// <summary>Resolves artifacts and creates an initialized client.</summary>
    public static async ValueTask<NeedleClient> CreateAsync(NeedleOptions? options = null,
        INeedleArtifactProvider? artifactProvider = null, ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        Validate(options);
        var ownsArtifacts = artifactProvider is null;
        artifactProvider ??= new HuggingFaceNeedleArtifactProvider(options);
        try
        {
            var artifacts = await artifactProvider.GetArtifactsAsync(cancellationToken).ConfigureAwait(false);
            return new(options, artifactProvider, ownsArtifacts, artifacts,
                (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<NeedleClient>());
        }
        catch
        {
            if (ownsArtifacts && artifactProvider is IDisposable disposable) disposable.Dispose();
            throw;
        }
    }

    /// <summary>Compiles <paramref name="input"/> using a short-lived session over <paramref name="tools"/>.</summary>
    public async ValueTask<ToolCallCompilation> CompileAsync(string input, IReadOnlyList<NeedleTool> tools,
        NeedleCompilationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        NeedleValidation.NativeText(input, nameof(input));
        NeedleValidation.CompilationOptions(options);
        await using var session = await CreateSessionAsync(tools, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await session.CompleteAsync(input, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a session holding the single live in-process lease; custom weights cannot later revert to base weights.</summary>
    public async ValueTask<INeedleSession> CreateSessionAsync(IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var toolSnapshot = NeedleValidation.Tools(tools);
        NeedleValidation.SessionOptions(options, _options);
        await RuntimeLease.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
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
            var result = NeedleNative.Init(facts, NeedleProtocol.SerializeTools(toolSnapshot), options?.ToolIndexPath ?? _options.ToolIndexPath);
            if (result < 0) throw new NeedleInitializationException($"needle_init failed with code {result}.");
            _logger.LogInformation("Needle session created with {ToolCount} tools.", toolSnapshot.Length);
            return new NeedleSession(toolSnapshot, _options, _logger, RuntimeLease, customWeights);
        }
        catch { RuntimeLease.Release(); throw; }
    }

    /// <summary>Obsolete shim that forwards to <see cref="CreateSessionAsync"/>.</summary>
    [Obsolete("Use CreateSessionAsync, which states what is created.")]
    public ValueTask<INeedleSession> CreateAsync(IReadOnlyList<NeedleTool> tools,
        NeedleSessionOptions? options = null, CancellationToken cancellationToken = default) =>
        CreateSessionAsync(tools, options, cancellationToken);

    /// <summary>Extracts a value of type <typeparamref name="T"/> by compiling against a synthesized extraction tool.</summary>
    [RequiresUnreferencedCode("Extraction reflects over the result type. Use the JsonTypeInfo overload for trimmed hosts.")]
    [RequiresDynamicCode("Extraction reflects over the result type. Use the JsonTypeInfo overload for NativeAOT hosts.")]
    public async ValueTask<NeedleExtractionResult<T>> ExtractAsync<T>(string input,
        NeedleExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var tool = NeedleTool.FromType<T>("extract", options?.Description ?? $"Extract a {typeof(T).Name} record from text");
        var compilation = await CompileAsync(input, [tool], new() { MaxNewTokens = options?.MaxNewTokens }, cancellationToken).ConfigureAwait(false);
        return NeedleExtractionResults.Create(compilation, "extract", arguments => arguments.Deserialize<T>(NeedleProtocol.Json), typeof(T).Name);
    }

    /// <summary>Extracts a record using serializer metadata instead of reflection.</summary>
    public async ValueTask<NeedleExtractionResult<T>> ExtractAsync<T>(string input,
        JsonTypeInfo<T> typeInfo,
        NeedleExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var tool = NeedleTool.FromType("extract", typeInfo, options?.Description ?? $"Extract a {typeof(T).Name} record from text",
            options?.NestedTypeResolver);
        var compilation = await CompileAsync(input, [tool], new() { MaxNewTokens = options?.MaxNewTokens }, cancellationToken).ConfigureAwait(false);
        return NeedleExtractionResults.Create(compilation, "extract", arguments => arguments.Deserialize(typeInfo), typeof(T).Name);
    }

    private const long MaxWeightsBytes = 32L * 1024 * 1024 * 1024;

    private static unsafe void LoadWeights(string path)
    {
        if (!File.Exists(path)) throw new NeedleArtifactNotFoundException($"Custom Needle weights were not found at '{path}'.");
        if (new FileInfo(path).Length > MaxWeightsBytes)
            throw new NeedleArtifactException($"Custom Needle weights exceed {MaxWeightsBytes} bytes.");
        var bytes = File.ReadAllBytes(path);
        try
        {
            fixed (byte* pointer = bytes)
            { var code = NeedleNative.LoadWeights(pointer, (ulong)bytes.LongLength); if (code != 0) throw new NeedleInitializationException($"needle_load failed with code {code}."); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void Validate(NeedleOptions options)
    {
        if (options.ResponseBufferSize < 1024) throw new ArgumentOutOfRangeException(nameof(options.ResponseBufferSize));
        if (options.DefaultMaxNewTokens <= 0) throw new ArgumentOutOfRangeException(nameof(options.DefaultMaxNewTokens));
        if (options.ExpectedNativeLibrarySha256 is not null &&
            (options.ExpectedNativeLibrarySha256.Length != 64 ||
             options.ExpectedNativeLibrarySha256.Any(character => !Uri.IsHexDigit(character))))
            throw new ArgumentException("Expected SHA-256 must contain exactly 64 hexadecimal characters.", nameof(options.ExpectedNativeLibrarySha256));
    }

    /// <summary>Marks the client as disposed; live sessions release the runtime lease on disposal.</summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsArtifacts && _artifacts is IDisposable disposable)
            disposable.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class NeedleSession : INeedleSession
{
    private readonly NeedleOptions _defaults;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lease;
    private readonly SemaphoreSlim _flight = new(1, 1);
    private readonly bool _customWeights;
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private int _disposed;
    public IReadOnlyList<NeedleTool> Tools { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    internal NeedleSession(IReadOnlyList<NeedleTool> tools, NeedleOptions defaults, ILogger logger, SemaphoreSlim lease, bool customWeights)
    { Tools = tools; _defaults = defaults; _logger = logger; _lease = lease; _customWeights = customWeights; }

    public async ValueTask<ToolCallCompilation> CompleteAsync(string input, NeedleCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        NeedleValidation.NativeText(input, nameof(input));
        NeedleValidation.CompilationOptions(options);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _flight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this); cancellationToken.ThrowIfCancellationRequested(); NeedleNative.Reset(); _logger.LogInformation("Needle session reset."); }
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
        lock (_disposeSync) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _flight.WaitAsync().ConfigureAwait(false);
        try { _lease.Release(); }
        finally { _flight.Release(); _flight.Dispose(); }
    }
}
