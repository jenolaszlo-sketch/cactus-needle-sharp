using System.Text.Json;
using CactusNeedleSharp;

return await WorkerHost.RunAsync();

internal static class WorkerHost
{
    private static readonly int MaximumRequestLength =
        int.TryParse(Environment.GetEnvironmentVariable("CACTUSNEEDLE_MAX_PROTOCOL_MESSAGE_LENGTH"), out var configured) && configured >= 1024
            ? configured
            : 1024 * 1024;

    internal static async Task<int> RunAsync()
    {
        INeedleSession? session = null;
        NeedleClient? client = null;
        try
        {
            while (await WorkerProtocol.ReadBoundedLineAsync(Console.In, MaximumRequestLength).ConfigureAwait(false) is { } line)
            {
                WorkerRequest? request = null;
                try
                {
                    request = JsonSerializer.Deserialize<WorkerRequest>(line, NeedleProtocol.Json)
                        ?? throw new NeedleWorkerException("Worker received an empty request.");
                    if (request.ProtocolVersion != WorkerProtocol.Version)
                        throw new NeedleWorkerException($"Unsupported worker protocol {request.ProtocolVersion}; expected {WorkerProtocol.Version}.");
                    object? payload = request.Operation switch
                    {
                        "ping" => new WorkerHandshake
                        {
                            ProtocolVersion = WorkerProtocol.Version,
                            WorkerVersion = typeof(WorkerHost).Assembly.GetName().Version?.ToString() ?? "unknown",
                            RuntimeFramework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                            Capabilities = ["sessions", "reset", "structured-output"]
                        },
                        "initialize" => await InitializeAsync(request),
                        "complete" => await CompleteAsync(request),
                        "reset" => await ResetAsync(),
                        "close-session" => await CloseSessionAsync(),
                        "shutdown" => new { shutdown = true },
                        _ => throw new NeedleWorkerException($"Unknown worker operation '{request.Operation}'.")
                    };
                    await RespondAsync(new WorkerResponse
                    {
                        Id = request.Id,
                        Success = true,
                        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, NeedleProtocol.Json)
                    }).ConfigureAwait(false);
                    if (request.Operation == "shutdown") return 0;
                }
                catch (Exception exception)
                {
                    await RespondAsync(new WorkerResponse
                    {
                        Id = request?.Id ?? string.Empty,
                        Success = false,
                        Error = exception.Message,
                        ErrorType = exception.GetType().Name
                    }).ConfigureAwait(false);
                }
            }
            return 0;
        }
        finally
        {
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            if (client is not null) await client.DisposeAsync().ConfigureAwait(false);
        }

        async Task<object> InitializeAsync(WorkerRequest request)
        {
            var initialization = request.Payload?.Deserialize<WorkerInitializePayload>(NeedleProtocol.Json)
                ?? throw new NeedleWorkerException("Initialize payload is missing.");
            if (session is not null) { await session.DisposeAsync().ConfigureAwait(false); session = null; }
            if (client is not null) { await client.DisposeAsync().ConfigureAwait(false); client = null; }
            client = await NeedleClient.CreateAsync(initialization.Runtime).ConfigureAwait(false);
            session = await client.CreateAsync(initialization.Tools, initialization.Session).ConfigureAwait(false);
            return new { initialized = true };
        }

        async Task<ToolCallCompilation> CompleteAsync(WorkerRequest request)
        {
            if (session is null) throw new NeedleWorkerException("Worker has no initialized session.");
            var completion = request.Payload?.Deserialize<WorkerCompletePayload>(NeedleProtocol.Json)
                ?? throw new NeedleWorkerException("Complete payload is missing.");
            return await session.CompleteAsync(completion.Input, completion.Options).ConfigureAwait(false);
        }

        async Task<object> ResetAsync()
        {
            if (session is null) throw new NeedleWorkerException("Worker has no initialized session.");
            await session.ResetAsync().ConfigureAwait(false);
            return new { reset = true };
        }

        async Task<object> CloseSessionAsync()
        {
            if (session is not null) { await session.DisposeAsync().ConfigureAwait(false); session = null; }
            if (client is not null) { await client.DisposeAsync().ConfigureAwait(false); client = null; }
            return new { closed = true };
        }
    }

    private static async Task RespondAsync(WorkerResponse response)
    {
        await Console.Out.WriteLineAsync(WorkerProtocol.Prefix + JsonSerializer.Serialize(response, NeedleProtocol.Json)).ConfigureAwait(false);
        await Console.Out.FlushAsync().ConfigureAwait(false);
    }
}
