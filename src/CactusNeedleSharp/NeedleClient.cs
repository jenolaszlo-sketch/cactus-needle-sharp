using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Reflection;
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
    private static string? LoadedModelHash;
    private static bool ModelLoadFailed;
    private readonly NeedleOptions _options;
    private readonly HuggingFaceNeedleArtifactProvider _models;
    private readonly bool _ownsModels;
    private readonly NeedleModelArtifact _defaultModel;
    private readonly bool _officialEngine;
    private readonly ILogger _logger;
    private int _disposed;

    /// <summary>Gets artifact metadata for the client's default model. Session overrides may select other weights.</summary>
    public NeedleRuntimeInfo RuntimeInfo { get; }

    private NeedleClient(NeedleOptions options, HuggingFaceNeedleArtifactProvider models, bool ownsModels,
        NeedleArtifacts engine, NeedleModelArtifact model, ILogger logger)
    {
        _options = options;
        _models = models;
        _ownsModels = ownsModels;
        _defaultModel = model;
        _logger = logger;
        NeedleNative.Load(engine);
        _officialEngine = engine.IntegrityVerified && engine.Revision == HuggingFaceNeedleArtifactProvider.EngineRevision;
        RuntimeInfo = new()
        {
            WrapperVersion = typeof(NeedleClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            RuntimeVersion = engine.Version,
            RuntimeRevision = engine.Revision,
            RuntimeSha256 = engine.Sha256,
            RuntimeIntegrityVerified = engine.IntegrityVerified,
        };
        RuntimeInfo = DescribeModel(model);
    }

    private NeedleRuntimeInfo DescribeModel(NeedleModelArtifact model) => RuntimeInfo with
    {
        ModelVersion = "needle3",
        ModelGeneration = 3,
        ModelSource = model.Source,
        ModelRevision = model.Revision,
        ModelSha256 = model.Sha256,
        ModelIntegrityVerified = model.IntegrityVerified,
        HasConfidenceHead = model.HasConfidenceHead,
        ConfidenceCalibrated = _officialEngine ? model.ConfidenceCalibrated : null
    };

    /// <summary>Resolves artifacts and creates an initialized client.</summary>
    public static async ValueTask<NeedleClient> CreateAsync(NeedleOptions? options = null,
        INeedleArtifactProvider? artifactProvider = null, ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        Validate(options);
        var models = artifactProvider as HuggingFaceNeedleArtifactProvider ?? new(options);
        var ownsModels = !ReferenceEquals(artifactProvider, models);
        try
        {
            var engine = await (artifactProvider ?? models).GetArtifactsAsync(cancellationToken).ConfigureAwait(false);
            var model = string.IsNullOrWhiteSpace(options.ModelPath)
                ? await models.GetModelArtifactsAsync(cancellationToken).ConfigureAwait(false)
                : await models.GetModelArtifactsAsync(options.ModelPath, cancellationToken, options.ExpectedModelSha256).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(options, models, ownsModels, engine, model,
                (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<NeedleClient>());
        }
        catch
        {
            if (ownsModels) models.Dispose();
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
            var model = string.IsNullOrWhiteSpace(options?.WeightsPath)
                ? _defaultModel
                : await _models.GetModelArtifactsAsync(options.WeightsPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (ModelLoadFailed)
                throw new NeedleInitializationException("A Cactus model load failed in this process. Create a fresh worker.");
            if (LoadedModelHash is not null && LoadedModelHash != model.Sha256)
                throw new NeedleInitializationException("Cactus already holds different weights in this process. Create a fresh worker for this model.");
            NeedleNative.SetThreads(_options.Threads);
            if (LoadedModelHash is null)
            {
                LoadWeights(model);
                LoadedModelHash = model.Sha256;
            }
            var facts = options?.SystemFacts ?? options?.Facts?.ToString();
            var result = NeedleNative.Init(facts, NeedleProtocol.SerializeTools(toolSnapshot), options?.ToolIndexPath ?? _options.ToolIndexPath);
            if (result < 0)
            {
                var error = NeedleNative.LastError();
                NeedleNative.Reset();
                throw new NeedleInitializationException($"cactus_init failed with code {result}: {error}");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                NeedleNative.Reset();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new NeedleSession(Array.AsReadOnly(toolSnapshot), _options, _logger, RuntimeLease, DescribeModel(model));
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

    private static unsafe void LoadWeights(NeedleModelArtifact model)
    {
        using var file = File.OpenRead(model.Path);
        if (file.Length > Array.MaxLength)
            throw new NeedleArtifactException($"Needle weights exceed the managed buffer limit of {Array.MaxLength} bytes.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        try
        {
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new NeedleArtifactException($"Needle weights changed after verification: '{model.Path}'.");
            using var archive = new MemoryStream(bytes, writable: false);
            NeedleModelFormat.Read(archive, model.Path);
            fixed (byte* pointer = bytes)
            {
                var code = NeedleNative.LoadWeights(pointer, (ulong)bytes.LongLength);
                if (code < 0)
                {
                    ModelLoadFailed = true;
                    throw new NeedleInitializationException($"cactus_load failed with code {code}: {NeedleNative.LastError()}");
                }
            }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void Validate(NeedleOptions options)
    {
        if (options.ResponseBufferSize < 1024) throw new ArgumentOutOfRangeException(nameof(options.ResponseBufferSize));
        if (options.DefaultMaxNewTokens <= 0) throw new ArgumentOutOfRangeException(nameof(options.DefaultMaxNewTokens));
        if (options.Threads < 0) throw new ArgumentOutOfRangeException(nameof(options.Threads));
        ValidateHash(options.ExpectedNativeLibrarySha256, nameof(options.ExpectedNativeLibrarySha256));
        ValidateHash(options.ExpectedModelSha256, nameof(options.ExpectedModelSha256));
    }

    private static void ValidateHash(string? hash, string parameter)
    {
        if (hash is not null && (hash.Length != 64 || !hash.All(Uri.IsHexDigit)))
            throw new ArgumentException("Expected SHA-256 must contain exactly 64 hexadecimal characters.", parameter);
    }

    /// <summary>Marks the client as disposed; live sessions release the runtime lease on disposal.</summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsModels)
            _models.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class NeedleSession : INeedleSession
{
    private readonly NeedleOptions _defaults;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lease;
    private readonly SemaphoreSlim _flight = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private int _disposed;
    public IReadOnlyList<NeedleTool> Tools { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public NeedleRuntimeInfo RuntimeInfo { get; }

    internal NeedleSession(IReadOnlyList<NeedleTool> tools, NeedleOptions defaults, ILogger logger, SemaphoreSlim lease, NeedleRuntimeInfo runtimeInfo)
    { Tools = tools; _defaults = defaults; _logger = logger; _lease = lease; RuntimeInfo = runtimeInfo; }

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
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            var buffer = new byte[_defaults.ResponseBufferSize];
            Array.Fill(buffer, (byte)0xff);
            var returnCode = CompleteNative(input, options?.MaxNewTokens ?? _defaults.DefaultMaxNewTokens, buffer);
            cancellationToken.ThrowIfCancellationRequested();
            var length = Array.IndexOf(buffer, (byte)0);
            if (returnCode < 0)
            {
                var detail = length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : NeedleNative.LastError();
                throw new NeedleInferenceException($"cactus_complete failed with code {returnCode}: {detail}");
            }
            if (length <= 0) throw new NeedleProtocolException("Needle returned an empty or unterminated response. Increase ResponseBufferSize if necessary.");
            var result = NeedleProtocol.Parse(buffer.AsSpan(0, length), Tools);
            if (RuntimeInfo.ConfidenceCalibrated != true) result = result with { Confidence = null };
            stopwatch.Stop();
            Record(result, stopwatch.Elapsed);
            _logger.LogInformation("Needle inference completed in {DurationMs}ms with {CallCount} calls and confidence {Confidence}.", stopwatch.Elapsed.TotalMilliseconds, result.Calls.Count, result.Confidence);
            return result;
        }
        finally { _flight.Release(); }
    }

    private static unsafe int CompleteNative(string input, int maxNewTokens, byte[] buffer)
    {
        fixed (byte* output = buffer)
            return NeedleNative.Complete(input, null, 0, maxNewTokens, output, buffer.Length);
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
        try { NeedleNative.Reset(); }
        finally
        {
            _lease.Release();
            _flight.Release();
        }
    }
}
