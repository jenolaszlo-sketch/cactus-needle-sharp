using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CactusNeedleSharp;

internal sealed class NeedleWorkerProcess : IAsyncDisposable
{
    private const string ProtocolPrefix = "@cactusneedlesharp:";
    private readonly Process _process;
    private readonly NeedleWorkerPoolOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _protocol = new(1, 1);
    private bool _disposed;

    internal bool IsHealthy => !_disposed && !_process.HasExited;
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
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start()) throw new NeedleWorkerException($"Failed to start Needle worker '{workerPath}'.");
        var worker = new NeedleWorkerProcess(process, options, logger);
        _ = worker.DrainStandardErrorAsync();
        try
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(options.StartupTimeout);
            await worker.SendAsync<JsonElement>("ping", null, startup.Token).ConfigureAwait(false);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _protocol.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), timeout.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
            string? responseLine;
            do
            {
                responseLine = await _process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (responseLine is null) throw new NeedleWorkerException("Needle worker closed its protocol stream unexpectedly.");
                if (!responseLine.StartsWith(ProtocolPrefix, StringComparison.Ordinal))
                    _logger.LogDebug("Needle worker {ProcessId} stdout: {Message}", _process.Id, responseLine);
            } while (!responseLine.StartsWith(ProtocolPrefix, StringComparison.Ordinal));
            var response = JsonSerializer.Deserialize<WorkerResponse>(responseLine[ProtocolPrefix.Length..], NeedleProtocol.Json)
                ?? throw new NeedleWorkerException("Needle worker returned an empty protocol response.");
            if (!string.Equals(response.Id, id, StringComparison.Ordinal))
                throw new NeedleWorkerException($"Needle worker response correlation mismatch: expected '{id}', received '{response.Id}'.");
            if (!response.Success) throw new NeedleWorkerException($"Needle worker {response.ErrorType ?? "error"}: {response.Error}");
            if (typeof(T) == typeof(JsonElement)) return (T)(object)(response.Payload ?? default(JsonElement));
            if (response.Payload is not { } responsePayload)
                throw new NeedleWorkerException($"Needle worker returned no {typeof(T).Name} payload.");
            var value = responsePayload.Deserialize<T>(NeedleProtocol.Json);
            if (value is null) throw new NeedleWorkerException($"Needle worker returned no {typeof(T).Name} payload.");
            return value;
        }
        catch (OperationCanceledException)
        {
            Kill();
            throw;
        }
        catch (NeedleWorkerException)
        {
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
        catch (Exception exception) when (_disposed || _process.HasExited)
        { _logger.LogTrace(exception, "Needle worker stderr stream closed."); }
    }

    private void Kill()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!_process.HasExited)
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
        finally { _protocol.Dispose(); _process.Dispose(); }
    }
}
