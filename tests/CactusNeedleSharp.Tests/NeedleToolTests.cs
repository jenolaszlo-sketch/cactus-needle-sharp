using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
    public void MissingConfidenceCannotSatisfyPolicy()
    {
        var compilation = new ToolCallCompilation
        {
            Success = true,
            Calls = [new NeedleToolCall { Name = "search", Arguments = JsonSerializer.SerializeToElement(new { }) }]
        };
        Assert.False(compilation.IsConfident(.8));
        Assert.Equal(NeedleCompilationOutcome.LowConfidence, compilation.GetOutcome(new() { MinimumConfidence = .8 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => compilation.GetOutcome(new() { MinimumConfidence = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ToolCallCompilation { Success = false }.GetOutcome(new() { MinimumConfidence = double.PositiveInfinity }));
    }

    [Fact]
    public void ExtractionRequiresOneNamedNonNullCall()
    {
        var noCall = new ToolCallCompilation { Success = true };
        var noCallResult = NeedleExtractionResults.Create(noCall, "extract", _ => new SearchArguments { Query = "x" }, "SearchArguments");
        Assert.False(noCallResult.Success);
        Assert.Equal(NeedleCompilationOutcome.NoCall, noCallResult.GetOutcome());

        var nullResult = NeedleExtractionResults.Create<SearchArguments>(
            new ToolCallCompilation
            {
                Success = true,
                Calls = [new NeedleToolCall { Name = "extract", Arguments = JsonSerializer.SerializeToElement(new { }) }]
            }, "extract", _ => null, "SearchArguments");
        Assert.False(nullResult.Success);
        Assert.Contains("null", nullResult.Error);
        Assert.Equal(NeedleCompilationOutcome.Failed, nullResult.GetOutcome(new() { MinimumConfidence = 0 }));

        var wrongName = NeedleExtractionResults.Create(
            new ToolCallCompilation
            {
                Success = true,
                Calls = [new NeedleToolCall { Name = "other", Arguments = JsonSerializer.SerializeToElement(new { query = "x" }) }],
                Confidence = 1
            }, "extract", element => element.Deserialize<SearchArguments>(), "SearchArguments");
        Assert.False(wrongName.Success);
        Assert.Equal(NeedleCompilationOutcome.Failed, wrongName.GetOutcome());
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
    public void MetadataUsesResolvedPropertyNamesAndDictionaryObjects()
    {
        var named = NeedleTool.FromType("named", TestJsonContext.Default.NamedArguments);
        Assert.True(named.Parameters.GetProperty("properties").TryGetProperty("EXACT_Name", out _));
        Assert.False(named.Parameters.GetProperty("properties").TryGetProperty("exacT_Name", out _));

        var dictionary = NeedleTool.FromType("map", TestJsonContext.Default.NamedArguments);
        var metadata = dictionary.Parameters.GetProperty("properties").GetProperty("values");
        Assert.Equal("object", metadata.GetProperty("type").GetString());
        Assert.Equal("string", metadata.GetProperty("additionalProperties").GetProperty("type").GetString());
        Assert.False(named.Parameters.GetProperty("properties").TryGetProperty("ignored", out _));

        var rootDictionary = NeedleTool.FromType("map", TestJsonContext.Default.StringMap);
        Assert.Equal("object", rootDictionary.Parameters.GetProperty("type").GetString());
        Assert.Equal("string", rootDictionary.Parameters.GetProperty("additionalProperties").GetProperty("type").GetString());
    }

    [Fact]
    public void ReflectionSchemasAndTypedDecodingShareEnumAndDictionaryContracts()
    {
        var numeric = NeedleTool.FromType<SearchArguments>("search");
        var numericPriority = numeric.Parameters.GetProperty("properties").GetProperty("priority");
        Assert.Equal("integer", numericPriority.GetProperty("type").GetString());
        var numericCall = new NeedleToolCall
        {
            Name = "search",
            Arguments = JsonSerializer.SerializeToElement(new { query = "needle", priority = 1 })
        };
        Assert.Equal(TaskPriority.High, numeric.DeserializeCallArguments(numericCall).Priority);

        var stringOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        stringOptions.Converters.Add(new JsonStringEnumConverter());
        var strings = NeedleTool.FromType<SearchArguments>("search", serializerOptions: stringOptions);
        var stringPriority = strings.Parameters.GetProperty("properties").GetProperty("priority");
        Assert.Equal("string", stringPriority.GetProperty("type").GetString());
        var stringCall = new NeedleToolCall
        {
            Name = "search",
            Arguments = JsonSerializer.SerializeToElement(new { query = "needle", priority = "High" })
        };
        Assert.Equal(TaskPriority.High, strings.DeserializeCallArguments(stringCall).Priority);
        var admitted = Assert.IsType<NeedleTool<SearchArguments>>(Assert.Single(NeedleValidation.Tools([strings])));
        Assert.Equal(TaskPriority.High, admitted.DeserializeCallArguments(stringCall).Priority);

        var dictionary = NeedleTool.FromType<ReadOnlyMapArguments>("map");
        Assert.Equal("object", dictionary.Parameters.GetProperty("properties").GetProperty("values").GetProperty("type").GetString());
        Assert.Throws<NeedleSchemaException>(() => NeedleTool.FromType<NonStringMapArguments>("invalid-map"));
    }

    [Fact]
    public void TypedToolRejectsCallsForAnotherTool()
    {
        var tool = NeedleTool.FromType<SearchArguments>("search");
        var call = new NeedleToolCall { Name = "other", Arguments = JsonSerializer.SerializeToElement(new { query = "needle" }) };
        Assert.Throws<NeedleProtocolException>(() => tool.DeserializeCallArguments(call));
    }

    [Fact]
    public void TypedToolSnapshotsMutableSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        var tool = NeedleTool.FromType<ContractArguments>("contract", serializerOptions: options);
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        var call = new NeedleToolCall
        {
            Name = "contract",
            Arguments = JsonSerializer.SerializeToElement(new { some_value = "retained" })
        };

        Assert.True(tool.Parameters.GetProperty("properties").TryGetProperty("some_value", out _));
        Assert.Equal("retained", tool.DeserializeCallArguments(call).SomeValue);
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
    private sealed record NamedArguments
    {
        [JsonPropertyName("EXACT_Name")] public string Name { get; init; } = "";
        public Dictionary<string, string> Values { get; init; } = new();
        [JsonIgnore] public string Ignored { get; init; } = "";
    }
    private sealed record ReadOnlyMapArguments(IReadOnlyDictionary<string, string> Values);
    private sealed record NonStringMapArguments(Dictionary<int, string> Values);
    private sealed record ContractArguments(string SomeValue);
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

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(SearchArguments))]
    [JsonSerializable(typeof(NestedArguments))]
    [JsonSerializable(typeof(NamedArguments))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
    [JsonSerializable(typeof(TaskPriority))]
    private sealed partial class TestJsonContext : JsonSerializerContext
    {
        public JsonTypeInfo<Dictionary<string, string>> StringMap => DictionaryStringString;
    }
}
