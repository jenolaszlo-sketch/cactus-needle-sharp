using System.Text.Json;

namespace CactusNeedleSharp;

internal sealed record WorkerRequest
{
    public required string Id { get; init; }
    public required string Operation { get; init; }
    public JsonElement? Payload { get; init; }
}

internal sealed record WorkerResponse
{
    public required string Id { get; init; }
    public required bool Success { get; init; }
    public JsonElement? Payload { get; init; }
    public string? Error { get; init; }
    public string? ErrorType { get; init; }
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
