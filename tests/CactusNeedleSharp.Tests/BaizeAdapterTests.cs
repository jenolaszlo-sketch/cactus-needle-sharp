using System.Text.Json;
using CactusNeedleSharp.Baize;
using CactusNeedleSharp.TestWorker;

namespace CactusNeedleSharp.Tests;

public sealed class BaizeAdapterTests
{
    [Fact]
    public async Task MaintainsConversationScopedWorkerSession()
    {
        await using var pool = new NeedleWorkerPool(new()
        {
            WorkerPath = typeof(Marker).Assembly.Location,
            MaximumWorkers = 1
        });
        var planner = new BaizeNeedlePlanner(pool, new() { MinimumConfidence = .8 });
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        await using var conversation = await planner.CreateConversationAsync([
            new BaizeToolDefinition { Name = "test", Parameters = schema.RootElement.Clone() }
        ]);

        var plan = await conversation.PlanAsync("use the test tool");

        Assert.Equal(NeedleCompilationOutcome.Success, plan.Outcome);
        Assert.Equal("test", Assert.Single(plan.Calls).Name);
        Assert.Equal(1, pool.WorkerCount);
    }
}
