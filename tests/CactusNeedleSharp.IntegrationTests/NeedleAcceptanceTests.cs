using CactusNeedleSharp;
using System.Runtime.InteropServices;
using Xunit;

namespace CactusNeedleSharp.IntegrationTests;

public sealed class NeedleAcceptanceTests
{
    [Fact]
    [Trait("Category", "NeedleIntegration")]
    public async Task CompilesRepositorySearchCall()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("NEEDLE_RUN_INTEGRATION_TESTS"));
        var expectedArchitecture = Environment.GetEnvironmentVariable("NEEDLE_EXPECTED_ARCHITECTURE");
        Assert.True(Enum.TryParse<Architecture>(expectedArchitecture, true, out var architecture));
        Assert.Equal(architecture, RuntimeInformation.ProcessArchitecture);

        var cacheDirectory = Environment.GetEnvironmentVariable("NEEDLE_TEST_CACHE_DIRECTORY")
            ?? Path.Combine(Path.GetTempPath(), "cactusneedlesharp-integration");
        var onlineOptions = new NeedleOptions { CacheDirectory = cacheDirectory };
        await using var needle = await NeedleClient.CreateAsync(onlineOptions);
        var tool = NeedleTool.FromJson("""{"name":"search_repository","description":"Search source code in a repository","parameters":{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}}""");
        var result = await needle.CompileAsync("Search the repository for WorkflowEngine usages", [tool]);
        Assert.True(result.Success); Assert.NotEmpty(result.Calls);
        Assert.Equal("search_repository", result.Calls[0].Name);
        Assert.Contains("WorkflowEngine", result.Calls[0].Arguments.GetProperty("query").GetString());
        Assert.NotNull(result.Confidence);

        await needle.DisposeAsync();
        await using var offlineNeedle = await NeedleClient.CreateAsync(onlineOptions with { Offline = true });
        var offlineResult = await offlineNeedle.CompileAsync("Search the repository for JsonRepair usages", [tool]);
        Assert.True(offlineResult.Success);
        Assert.NotEmpty(offlineResult.Calls);
    }

    [Fact]
    [Trait("Category", "NeedleIntegration")]
    public async Task PoolBoundsAndIsolatesConcurrentConversations()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("NEEDLE_RUN_INTEGRATION_TESTS"));
        var workerPath = Environment.GetEnvironmentVariable("NEEDLE_WORKER_PATH");
        Assert.True(File.Exists(workerPath), $"Set NEEDLE_WORKER_PATH to the built worker executable or DLL. Received '{workerPath}'.");
        var cacheDirectory = Environment.GetEnvironmentVariable("NEEDLE_TEST_CACHE_DIRECTORY")
            ?? Path.Combine(Path.GetTempPath(), "cactusneedlesharp-integration");
        await using var pool = new NeedleWorkerPool(new()
        {
            WorkerPath = workerPath!,
            MaximumWorkers = 2,
            Runtime = new NeedleOptions { CacheDirectory = cacheDirectory }
        });

        var search = NeedleTool.FromJson("""{"name":"search_repository","description":"Search source code in a repository","parameters":{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}}""");
        var weather = NeedleTool.FromJson("""{"name":"get_weather","description":"Get current weather for a city","parameters":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}}""");
        await using var sessionA = await pool.CreateSessionAsync([search]);
        await using var sessionB = await pool.CreateSessionAsync([weather]);
        Assert.Equal(2, pool.WorkerCount);

        var results = await Task.WhenAll(
            sessionA.CompleteAsync("Search for NeedleWorkerPool usages").AsTask(),
            sessionB.CompleteAsync("What is the weather in Manila?").AsTask());
        Assert.Equal("search_repository", Assert.Single(results[0].Calls).Name);
        Assert.Equal("get_weather", Assert.Single(results[1].Calls).Name);

        var pending = pool.CreateSessionAsync([search]).AsTask();
        await Task.Delay(200);
        Assert.False(pending.IsCompleted, "A third conversation must wait while both workers are leased.");
        await sessionA.DisposeAsync();
        await using var sessionC = await pending;
        Assert.Equal(2, pool.WorkerCount);
        var reused = await sessionC.CompleteAsync("Search for JsonRepair usages");
        Assert.Equal("search_repository", Assert.Single(reused.Calls).Name);
    }
}
