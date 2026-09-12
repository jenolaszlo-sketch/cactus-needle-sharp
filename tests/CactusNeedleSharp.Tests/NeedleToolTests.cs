using System.Text.Json;
using System.Text.Json.Serialization;
using CactusNeedleSharp;

namespace CactusNeedleSharp.Tests;

public sealed partial class NeedleToolTests
{
    [Fact]
    public void RawJsonPreservesSchema()
    {
        var tool = NeedleTool.FromJson("""{"name":"search","parameters":{"type":"object","properties":{"q":{"type":"string","x-future":42}},"required":["q"]}}""");
        Assert.Equal("search", tool.Name);
        Assert.Equal(42, tool.Parameters.GetProperty("properties").GetProperty("q").GetProperty("x-future").GetInt32());
    }

    [Fact]
    public void TypedToolCreatesObjectSchema()
    {
        var tool = NeedleTool.FromType<SearchArguments>("search");
        Assert.Equal("object", tool.Parameters.GetProperty("type").GetString());
        Assert.True(tool.Parameters.GetProperty("properties").TryGetProperty("query", out _));
        Assert.Contains(tool.Parameters.GetProperty("required").EnumerateArray(), x => x.GetString() == "query");
    }

    [Fact]
    public void ProtocolPreservesArgumentsAndNullableConfidence()
    {
        var result = NeedleProtocol.Parse("""{"success":true,"function_calls":[{"name":"search","arguments":{"q":1}}],"future":true}"""u8);
        Assert.Null(result.Confidence);
        Assert.Equal(1, result.Calls[0].Arguments.GetProperty("q").GetInt32());
    }

    [Fact]
    public void ProtocolRejectsMalformedJson() => Assert.Throws<NeedleProtocolException>(() => NeedleProtocol.Parse("{"u8));

    [Fact]
    public void TypedArgumentsAndOutcomeAreExplicit()
    {
        var call = new NeedleToolCall { Name = "search", Arguments = JsonSerializer.SerializeToElement(new { query = "needle" }) };
        Assert.Equal("needle", call.DeserializeArguments<SearchArguments>().Query);
        var compilation = new ToolCallCompilation { Success = true, Calls = [call], Confidence = .2 };
        Assert.Equal(NeedleCompilationOutcome.LowConfidence, compilation.GetOutcome(new() { MinimumConfidence = .8 }));
    }

    [Fact]
    public void MetadataToolCreatesObjectSchemaWithoutReflection()
    {
        var tool = NeedleTool.FromType("search", TestJsonContext.Default.SearchArguments);
        Assert.Equal("object", tool.Parameters.GetProperty("type").GetString());
        Assert.True(tool.Parameters.GetProperty("properties").TryGetProperty("query", out _));
        Assert.Contains(tool.Parameters.GetProperty("required").EnumerateArray(), x => x.GetString() == "query");
        var priority = tool.Parameters.GetProperty("properties").GetProperty("priority");
        Assert.Equal("string", priority.GetProperty("type").GetString());
        Assert.Contains(priority.GetProperty("enum").EnumerateArray(), x => x.GetString() == "High");
    }

    [Fact]
    public void MetadataNestedTypesResolveThroughResolver()
    {
        var tool = NeedleTool.FromType(
            "search",
            TestJsonContext.Default.NestedArguments,
            nested: static type => type == typeof(SearchArguments)
                ? TestJsonContext.Default.SearchArguments
                : null);
        Assert.Equal(
            "object",
            tool.Parameters.GetProperty("properties").GetProperty("inner").GetProperty("type").GetString());
    }

    [Fact]
    public void MetadataNestedTypesWithoutResolverFailExplicitly()
    {
        Assert.Throws<NeedleSchemaException>(() =>
            NeedleTool.FromType("search", TestJsonContext.Default.NestedArguments));
    }

    [Fact]
    public void MetadataDeserializeRoundTrip()
    {
        var original = new SearchArguments { Query = "needle" };
        var call = new NeedleToolCall
        {
            Name = "search",
            Arguments = JsonSerializer.SerializeToElement(original, TestJsonContext.Default.SearchArguments)
        };
        Assert.Equal(original, call.DeserializeArguments(TestJsonContext.Default.SearchArguments));
        Assert.True(call.TryDeserializeArguments(out SearchArguments? value, out var error, TestJsonContext.Default.SearchArguments));
        Assert.Null(error);
        Assert.Equal(original, value);
    }

    private sealed record SearchArguments
    {
        public required string Query { get; init; }
        public string? Path { get; init; }
        public TaskPriority Priority { get; init; } = TaskPriority.Low;
    }
    private sealed record NestedArguments(string Name, SearchArguments Inner);
    private enum TaskPriority { Low, High }

    [Fact]
    public void WorkerCompilationPayloadRoundTripsThroughContext()
    {
        var compilation = new ToolCallCompilation
        {
            Success = true,
            Calls = [new NeedleToolCall { Name = "search", Arguments = JsonDocument.Parse("{}").RootElement.Clone() }],
            Confidence = .9
        };
        var response = new WorkerResponse
        {
            Id = "test",
            Success = true,
            Payload = JsonSerializer.SerializeToElement(compilation, NeedleJsonContext.Default.ToolCallCompilation)
        };
        var line = JsonSerializer.Serialize(response, NeedleJsonContext.Default.WorkerResponse);
        var back = JsonSerializer.Deserialize(line, NeedleJsonContext.Default.WorkerResponse);
        Assert.NotNull(back);
        var payload = back.Payload!.Value.Deserialize(NeedleJsonContext.Default.ToolCallCompilation);
        Assert.NotNull(payload);
        Assert.True(payload.Success);
        Assert.Equal("search", Assert.Single(payload.Calls).Name);
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(SearchArguments))]
    [JsonSerializable(typeof(NestedArguments))]
    [JsonSerializable(typeof(TaskPriority))]
    private sealed partial class TestJsonContext : JsonSerializerContext
    {
    }
}
