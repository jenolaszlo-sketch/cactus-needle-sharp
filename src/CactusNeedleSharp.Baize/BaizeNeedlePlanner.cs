using System.Text.Json;

namespace CactusNeedleSharp.Baize;

public sealed record BaizeToolDefinition
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required JsonElement Parameters { get; init; }

    internal NeedleTool ToNeedleTool() => NeedleTool.Create(Name, Description, Parameters);
}

public sealed record BaizeToolPlan
{
    public required NeedleCompilationOutcome Outcome { get; init; }
    public IReadOnlyList<NeedleToolCall> Calls { get; init; } = [];
    public double? Confidence { get; init; }
    public string? Reasoning { get; init; }
    public string? Error { get; init; }
}

public interface IBaizeToolConversation : IAsyncDisposable
{
    ValueTask<BaizeToolPlan> PlanAsync(string message, CancellationToken cancellationToken = default);
    ValueTask ResetAsync(CancellationToken cancellationToken = default);
}

public interface IBaizeToolPlanner
{
    ValueTask<IBaizeToolConversation> CreateConversationAsync(IReadOnlyList<BaizeToolDefinition> tools,
        NeedleSessionOptions? sessionOptions = null, CancellationToken cancellationToken = default);
}

public sealed class BaizeNeedlePlanner : IBaizeToolPlanner
{
    private readonly INeedleWorkerPool _workers;
    private readonly NeedleConfidencePolicy _confidence;

    public BaizeNeedlePlanner(INeedleWorkerPool workers, NeedleConfidencePolicy? confidence = null)
    {
        _workers = workers;
        _confidence = confidence ?? new();
    }

    public async ValueTask<IBaizeToolConversation> CreateConversationAsync(IReadOnlyList<BaizeToolDefinition> tools,
        NeedleSessionOptions? sessionOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var session = await _workers.CreateSessionAsync(tools.Select(tool => tool.ToNeedleTool()).ToArray(),
            sessionOptions, cancellationToken).ConfigureAwait(false);
        return new Conversation(session, _confidence);
    }

    private sealed class Conversation(INeedleSession session, NeedleConfidencePolicy confidence) : IBaizeToolConversation
    {
        public async ValueTask<BaizeToolPlan> PlanAsync(string message, CancellationToken cancellationToken = default)
        {
            var compilation = await session.CompleteAsync(message, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new()
            {
                Outcome = compilation.GetOutcome(confidence),
                Calls = compilation.Calls,
                Confidence = compilation.Confidence,
                Reasoning = compilation.Reasoning,
                Error = compilation.Error
            };
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken = default) => session.ResetAsync(cancellationToken);
        public ValueTask DisposeAsync() => session.DisposeAsync();
    }
}
