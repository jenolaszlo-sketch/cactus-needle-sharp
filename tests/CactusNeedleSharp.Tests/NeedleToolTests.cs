using System.Text.Json;
using CactusNeedleSharp;

namespace CactusNeedleSharp.Tests;

public sealed class NeedleToolTests
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

    private sealed record SearchArguments(string Query, string? Path);
}
