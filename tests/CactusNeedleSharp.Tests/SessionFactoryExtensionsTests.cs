using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CactusNeedleSharp.Tests;

public sealed partial class SessionFactoryExtensionsTests
{
    [Fact]
    public async Task MetadataExtractionUsesNestedResolverAndDisposesSession()
    {
        var factory = new FakeSessionFactory(JsonSerializer.SerializeToElement(
            new Envelope(new Payload("needle")), TestContext.Default.Envelope));
        var options = new NeedleExtractionOptions
        {
            NestedTypeResolver = type => type == typeof(Payload) ? TestContext.Default.Payload : null
        };

        var result = await factory.ExtractAsync("extract it", TestContext.Default.Envelope, options);

        Assert.True(result.Success);
        Assert.Equal("needle", result.Value!.Payload.Value);
        Assert.True(factory.SessionDisposed);
        Assert.Equal("object", factory.Tools![0].Parameters.GetProperty("properties").GetProperty("payload").GetProperty("type").GetString());
    }

    [Fact]
    public async Task InvalidOneShotInputDoesNotCreateSession()
    {
        var factory = new FakeSessionFactory(JsonSerializer.SerializeToElement(new { }));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CompileAsync(" ", [NeedleTool.FromType<Payload>("payload")]).AsTask());

        Assert.Null(factory.Tools);
    }

    private sealed class FakeSessionFactory(JsonElement arguments) : INeedleSessionFactory
    {
        public IReadOnlyList<NeedleTool>? Tools { get; private set; }
        public bool SessionDisposed { get; private set; }

        public ValueTask<INeedleSession> CreateSessionAsync(IReadOnlyList<NeedleTool> tools,
            NeedleSessionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Tools = tools;
            return ValueTask.FromResult<INeedleSession>(new FakeSession(this, tools, arguments));
        }

        private sealed class FakeSession(FakeSessionFactory owner, IReadOnlyList<NeedleTool> tools, JsonElement arguments) : INeedleSession
        {
            public string SessionId { get; } = "fake";
            public IReadOnlyList<NeedleTool> Tools { get; } = tools;
            public ValueTask<ToolCallCompilation> CompleteAsync(string input, NeedleCompilationOptions? options = null,
                CancellationToken cancellationToken = default) => ValueTask.FromResult(new ToolCallCompilation
                {
                    Success = true,
                    Confidence = 1,
                    Calls = [new NeedleToolCall { Name = Tools[0].Name, Arguments = arguments }]
                });
            public ValueTask ResetAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask DisposeAsync() { owner.SessionDisposed = true; return ValueTask.CompletedTask; }
        }
    }

    private sealed record Envelope(Payload Payload);
    private sealed record Payload(string Value);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Envelope))]
    [JsonSerializable(typeof(Payload))]
    private sealed partial class TestContext : JsonSerializerContext;
}
