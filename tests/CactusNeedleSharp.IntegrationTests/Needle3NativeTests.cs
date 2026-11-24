using System.Buffers.Binary;
using System.Text.Json.Serialization;
using CactusNeedleSharp;
using Xunit;

namespace CactusNeedleSharp.IntegrationTests;

public sealed partial class Needle3NativeTests
{
    private static string Cache => Environment.GetEnvironmentVariable("NEEDLE_TEST_CACHE_DIRECTORY") ??
        Path.Combine(Path.GetTempPath(), "cactusneedlesharp-integration");
    private static NeedleOptions Runtime => new() { CacheDirectory = Cache, Threads = 4 };
    private static readonly NeedleTool Weather = NeedleTool.FromJson("""
        {"name":"get_weather","description":"Get current weather for a city","parameters":{"type":"object",
         "properties":{"city":{"type":"string"}},"required":["city"],"additionalProperties":false}}
        """);
    private static readonly NeedleTool Search = NeedleTool.FromJson("""
        {"name":"search_repository","description":"Search source code in a repository","parameters":{"type":"object",
         "properties":{"query":{"type":"string"}},"required":["query"],"additionalProperties":false}}
        """);

    [NeedleIntegrationFact]
    [Trait("Category", "NeedleIntegration")]
    public async Task NativeEnvelopeDistinguishesSuppressionNegationAndNoCall()
    {
        await using var client = await NeedleClient.CreateAsync(Runtime);
        Assert.Equal("4.1.4", client.RuntimeInfo.RuntimeVersion);
        Assert.Equal(3, client.RuntimeInfo.ModelGeneration);
        Assert.True(client.RuntimeInfo.ConfidenceCalibrated);
        await using var session = await client.CreateSessionAsync([Weather]);
        var call = await session.CompleteAsync("What is the weather in Paris?");
        Assert.Equal(NeedleCompilationOutcome.Success, call.GetOutcome());
        Assert.Equal("Paris", Assert.Single(call.Calls).Arguments.GetProperty("city").GetString());
        Assert.Equal(NeedleCompilationOutcome.LowConfidence, call.GetOutcome(new() { MinimumConfidence = 1 }));

        var followup = await session.CompleteAsync("What about London?");
        Assert.Equal("London", Assert.Single(followup.Calls).Arguments.GetProperty("city").GetString());
        await session.ResetAsync();
        var suppressed = await session.CompleteAsync("Get the weather");
        Assert.Empty(suppressed.Calls);
        Assert.Single(suppressed.SuppressedCalls);
        Assert.Equal(NeedleCompilationOutcome.Suppressed, suppressed.GetOutcome());

        await session.ResetAsync();
        var negated = await session.CompleteAsync("Do not look up the weather in Paris");
        Assert.Empty(negated.Calls);
        Assert.Equal(NeedleCompilationOutcome.Suppressed, negated.GetOutcome());
        Assert.True(negated.Validation!.Negation);

        await session.ResetAsync();
        var absent = await session.CompleteAsync("Translate this into Spanish");
        Assert.Equal(NeedleCompilationOutcome.NoCall, absent.GetOutcome());
        Assert.Empty(absent.SuppressedCalls);

        await session.ResetAsync();
        var unicode = await session.CompleteAsync("How is the weather in São Paulo?");
        Assert.Equal("São Paulo", Assert.Single(unicode.Calls).Arguments.GetProperty("city").GetString());
        await Assert.ThrowsAsync<ArgumentException>(() => session.CompleteAsync("bad\0input").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => session.CompleteAsync("bad\ud800input").AsTask());
    }

    [NeedleIntegrationFact]
    [Trait("Category", "NeedleIntegration")]
    public async Task NativeSelectsAcrossToolsAndExtractsUsingGeneratedMetadata()
    {
        await using var client = await NeedleClient.CreateAsync(Runtime);
        var search = await client.CompileAsync("Search the repository for WorkflowEngine usages", [Weather, Search]);
        Assert.Equal("search_repository", Assert.Single(search.Calls).Name);
        var extraction = await client.ExtractAsync("The recipient is Alice.", NativeJsonContext.Default.Recipient,
            new() { Description = "Extract the recipient name from text" });
        Assert.True(extraction.Success, extraction.Error ?? extraction.Compilation.Reasoning);
        Assert.Equal("Alice", extraction.Value!.Name);
    }

    [NeedleIntegrationFact]
    [Trait("Category", "NeedleIntegration")]
    public async Task CompatibleExplicitWeightsUseFreshWorkerAndPreserveBaseWorkerCapacity()
    {
        using var provider = new HuggingFaceNeedleArtifactProvider(Runtime);
        var model = await provider.GetModelArtifactsAsync();
        var bytes = await File.ReadAllBytesAsync(model.Path);
        var codebook = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        var directory = checked((int)(196 + codebook * 4));
        var end = checked(directory + (int)count * 44);
        var firstTensor = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(directory + 20));
        Assert.True((ulong)end < firstTensor);
        bytes[end] ^= 1; // Distinct digest with the verified model's actual tensor payload unchanged.
        var path = Path.Combine(Path.GetDirectoryName(model.Path)!, Guid.NewGuid() + ".cact");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var workerPath = Environment.GetEnvironmentVariable("NEEDLE_WORKER_PATH");
            Assert.True(File.Exists(workerPath));
            await using var pool = new NeedleWorkerPool(new() { WorkerPath = workerPath!, MaximumWorkers = 1, Runtime = Runtime with { Offline = true } });
            await pool.WarmAsync(1);
            await using (var baseline = await pool.CreateSessionAsync([Weather]))
                Assert.NotNull((await baseline.CompleteAsync("What is the weather in Paris?")).Confidence);
            Assert.Equal(1, pool.IdleWorkerCount);
            await using (var custom = await pool.CreateSessionAsync([Weather], new() { WeightsPath = path }))
            {
                var result = await custom.CompleteAsync("What is the weather in Paris?");
                Assert.Single(result.Calls);
                Assert.Null(result.Confidence);
                Assert.Equal(NeedleCompilationOutcome.LowConfidence, result.GetOutcome());
            }
            Assert.Equal(0, pool.WorkerCount);
            for (var repeat = 0; repeat < 5; repeat++)
            {
                await using var baseline = await pool.CreateSessionAsync([Weather]);
                var result = await baseline.CompleteAsync("What is the weather in Paris?");
                Assert.Equal("Paris", Assert.Single(result.Calls).Arguments.GetProperty("city").GetString());
                Assert.NotNull(result.Confidence);
            }
        }
        finally { File.Delete(path); }
    }

    public sealed record Recipient(string Name);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Recipient))]
    private sealed partial class NativeJsonContext : JsonSerializerContext;
}

