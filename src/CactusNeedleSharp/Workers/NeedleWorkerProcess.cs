using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CactusNeedleSharp;

internal sealed class NeedleWorkerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly NeedleWorkerPoolOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _protocol = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private int _disposed;

    internal DateTimeOffset LastUsedAt { get; private set; } = DateTimeOffset.UtcNow;

    internal bool IsHealthy => Volatile.Read(ref _disposed) == 0 && !_process.HasExited;
    internal int ProcessId => IsHealthy ? _process.Id : -1;

    private NeedleWorkerProcess(Process process, NeedleWorkerPoolOptions options, ILogger logger)
    { _process = process; _options = options; _logger = logger; }

    internal static async ValueTask<NeedleWorkerProcess> StartAsync(
        NeedleWorkerPoolOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        var suppliedPath = options.WorkerPath;
        var isDll = suppliedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var requiresFile = isDll || Path.IsPathRooted(suppliedPath) || suppliedPath.Contains(Path.DirectorySeparatorChar) || suppliedPath.Contains(Path.AltDirectorySeparatorChar);
        var workerPath = requiresFile ? Path.GetFullPath(suppliedPath) : suppliedPath;
        if (requiresFile && !File.Exists(workerPath)) throw new NeedleWorkerException($"Needle worker was not found at '{workerPath}'.");
        var start = new ProcessStartInfo
        {
            FileName = isDll ? "dotnet" : workerPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (isDll) start.ArgumentList.Add(workerPath);
        foreach (var argument in options.WorkerArguments) start.ArgumentList.Add(argument);
        start.Environment["CACTUSNEEDLE_MAX_PROTOCOL_MESSAGE_LENGTH"] = options.MaximumProtocolMessageLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start()) throw new NeedleWorkerException($"Failed to start Needle worker '{workerPath}'.");
        var worker = new NeedleWorkerProcess(process, options, logger);
        _ = worker.DrainStandardErrorAsync();
        try
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(options.StartupTimeout);
            var handshake = await worker.SendAsync<WorkerHandshake>("ping", null, startup.Token).ConfigureAwait(false);
            if (handshake.ProtocolVersion != WorkerProtocol.Version)
                throw new NeedleWorkerException($"Needle worker protocol {handshake.ProtocolVersion} is incompatible with client protocol {WorkerProtocol.Version}.");
            logger.LogInformation("Started Needle worker process {ProcessId}.", process.Id);
            return worker;
        }
        catch
        {
            await worker.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async ValueTask InitializeAsync(IReadOnlyList<NeedleTool> tools, NeedleSessionOptions? session,
        CancellationToken cancellationToken) =>
        _ = await SendAsync<JsonElement>("initialize", new WorkerInitializePayload
        { Runtime = _options.Runtime, Tools = tools.ToArray(), Session = session }, cancellationToken).ConfigureAwait(false);

    internal ValueTask<ToolCallCompilation> CompleteAsync(string input, NeedleCompilationOptions? options,
        CancellationToken cancellationToken) =>
        SendAsync<ToolCallCompilation>("complete", new WorkerCompletePayload { Input = input, Options = options }, cancellationToken);

    internal async ValueTask ResetAsync(CancellationToken cancellationToken) =>
        _ = await SendAsync<JsonElement>("reset", null, cancellationToken).ConfigureAwait(false);

    internal async ValueTask CloseSessionAsync(CancellationToken cancellationToken) =>
        _ = await SendAsync<JsonElement>("close-session", null, cancellationToken).ConfigureAwait(false);

    private async ValueTask<T> SendAsync<T>(string operation, object? payload, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _protocol.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_process.HasExited) throw new NeedleWorkerException($"Needle worker exited with code {_process.ExitCode}.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);
            var id = Guid.NewGuid().ToString("N");
            var request = new WorkerRequest
            {
                Id = id,
                Operation = operation,
                Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, NeedleProtocol.Json)
            };
            var line = JsonSerializer.Serialize(request, NeedleProtocol.Json);
            if (line.Length > _options.MaximumProtocolMessageLength)
                throw new NeedleWorkerException($"Needle worker request exceeded the {_options.MaximumProtocolMessageLength}-character protocol limit.");
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), timeout.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
            string? responseLine;
            do
            {
                responseLine = await WorkerProtocol.ReadBoundedLineAsync(
                    _process.StandardOutput, _options.MaximumProtocolMessageLength, timeout.Token).ConfigureAwait(false);
                if (responseLine is null) throw new NeedleWorkerException("Needle worker closed its protocol stream unexpectedly.");
                if (!responseLine.StartsWith(WorkerProtocol.Prefix, StringComparison.Ordinal))
                    _logger.LogDebug("Needle worker {ProcessId} stdout: {Message}", _process.Id, responseLine);
            } while (!responseLine.StartsWith(WorkerProtocol.Prefix, StringComparison.Ordinal));
            var response = JsonSerializer.Deserialize<WorkerResponse>(responseLine[WorkerProtocol.Prefix.Length..], NeedleProtocol.Json)
                ?? throw new NeedleWorkerException("Needle worker returned an empty protocol response.");
            if (response.ProtocolVersion != WorkerProtocol.Version)
                throw new NeedleWorkerException($"Needle worker response uses protocol {response.ProtocolVersion}; expected {WorkerProtocol.Version}.");
            if (!string.Equals(response.Id, id, StringComparison.Ordinal))
                throw new NeedleWorkerException($"Needle worker response correlation mismatch: expected '{id}', received '{response.Id}'.");
            if (!response.Success) throw new NeedleWorkerException($"Needle worker {response.ErrorType ?? "error"}: {response.Error}");
            LastUsedAt = DateTimeOffset.UtcNow;
            if (typeof(T) == typeof(JsonElement)) return (T)(object)(response.Payload ?? default(JsonElement));
            if (response.Payload is not { } responsePayload)
                throw new NeedleWorkerException($"Needle worker returned no {typeof(T).Name} payload.");
            var value = responsePayload.Deserialize<T>(NeedleProtocol.Json);
            if (value is null) throw new NeedleWorkerException($"Needle worker returned no {typeof(T).Name} payload.");
            return value;
        }
        catch (OperationCanceledException)
        {
            NeedleDiagnostics.WorkerFailures.Add(1, new KeyValuePair<string, object?>("reason", "canceled"));
            Kill();
            throw;
        }
        catch (NeedleWorkerException)
        {
            NeedleDiagnostics.WorkerFailures.Add(1, new KeyValuePair<string, object?>("reason", "protocol"));
            Kill();
            throw;
        }
        catch (Exception exception)
        {
            Kill();
            throw new NeedleWorkerException($"Needle worker protocol operation '{operation}' failed.", exception);
        }
        finally { _protocol.Release(); }
    }

    private async Task DrainStandardErrorAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                _logger.LogDebug("Needle worker {ProcessId}: {Message}", _process.Id, line);
        }
        catch (Exception exception) when (Volatile.Read(ref _disposed) != 0 || _process.HasExited)
        { _logger.LogTrace(exception, "Needle worker stderr stream closed."); }
    }

    private void Kill()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var ownsProtocol = _protocol.Wait(0);
        try
        {
            if (!ownsProtocol)
            {
                Kill();
                using var exclusiveTimeout = new CancellationTokenSource(_options.ShutdownTimeout);
                try
                {
                    await _protocol.WaitAsync(exclusiveTimeout.Token).ConfigureAwait(false);
                    ownsProtocol = true;
                }
                catch (OperationCanceledException) { }
            }
            else if (!_process.HasExited)
            {
                try
                {
                    var request = JsonSerializer.Serialize(new WorkerRequest { Id = "shutdown", Operation = "shutdown" }, NeedleProtocol.Json);
                    await _process.StandardInput.WriteLineAsync(request).ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                    using var timeout = new CancellationTokenSource(_options.ShutdownTimeout);
                    await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { Kill(); }
                catch (IOException) { Kill(); }
            }
        }
        finally
        {
            if (ownsProtocol) _protocol.Release();
            _process.Dispose();
        }
    }
}
