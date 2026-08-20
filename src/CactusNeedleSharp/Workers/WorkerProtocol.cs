using System.Text.Json;
using System.Text;

namespace CactusNeedleSharp;

internal static class WorkerProtocol
{
    internal const int Version = 1;
    internal const string Prefix = "@cactusneedlesharp:";

    internal static async ValueTask<string?> ReadBoundedLineAsync(TextReader reader, int maximumLength,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder(Math.Min(maximumLength, 4096));
        var character = new char[1];
        while (true)
        {
            var read = await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) return builder.Length == 0 ? null : builder.ToString();
            if (character[0] == '\n') return builder.ToString();
            if (character[0] == '\r') continue;
            if (builder.Length >= maximumLength)
                throw new NeedleWorkerException($"Worker protocol line exceeded the {maximumLength}-character limit.");
            builder.Append(character[0]);
        }
    }
}

internal sealed record WorkerRequest
{
    public int ProtocolVersion { get; init; } = WorkerProtocol.Version;
    public required string Id { get; init; }
    public required string Operation { get; init; }
    public JsonElement? Payload { get; init; }
}

internal sealed record WorkerResponse
{
    public int ProtocolVersion { get; init; } = WorkerProtocol.Version;
    public required string Id { get; init; }
    public required bool Success { get; init; }
    public JsonElement? Payload { get; init; }
    public string? Error { get; init; }
    public string? ErrorType { get; init; }
}

internal sealed record WorkerHandshake
{
    public required int ProtocolVersion { get; init; }
    public required string WorkerVersion { get; init; }
    public required string RuntimeFramework { get; init; }
    public string[] Capabilities { get; init; } = [];
}

internal sealed record WorkerInitializePayload
{
    public required NeedleOptions Runtime { get; init; }
    public required NeedleTool[] Tools { get; init; }
    public NeedleSessionOptions? Session { get; init; }
}

internal sealed record WorkerCompletePayload
{
    public required string Input { get; init; }
    public NeedleCompilationOptions? Options { get; init; }
}
