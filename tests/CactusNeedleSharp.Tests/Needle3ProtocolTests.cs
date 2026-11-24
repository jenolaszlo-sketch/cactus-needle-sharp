using System.Text;
using System.Text.Json;
using CactusNeedleSharp;

namespace CactusNeedleSharp.Tests;

public sealed class Needle3ProtocolTests
{
    private static readonly NeedleTool Tool = NeedleTool.FromJson("""
        {"name":"set","parameters":{"type":"object","properties":{
          "mode":{"enum":["eco","boost"]},"level":{"type":"integer","minimum":1,"maximum":5},
          "label":{"type":"string","minLength":2,"maxLength":8}
        },"required":["mode","level"],"additionalProperties":false}}
        """);

    [Theory]
    [InlineData("""{"mode":"eco","level":2}""", true)]
    [InlineData("""{"mode":"eco","level":2,"label":"🌵a"}""", true)]
    [InlineData("""{"mode":"other","level":2}""", false)]
    [InlineData("""{"mode":"eco","level":2.5}""", false)]
    [InlineData("""{"mode":"eco","level":7}""", false)]
    [InlineData("""{"mode":"eco","label":"okay"}""", false)]
    [InlineData("""{"mode":"eco","level":2,"label":"z"}""", false)]
    [InlineData("""{"mode":"eco","level":2,"extra":true}""", false)]
    public void ChecksArgumentsBeforeAnOutcomeCanSucceed(string arguments, bool accepted)
    {
        var result = Parse($$"""
            {"type":"call","success":true,"function_calls":[{"name":"set","arguments":{{arguments}}}],
             "suppressed_calls":[],"confidence":0.95}
            """);
        Assert.Equal(accepted, result.IsConfident(.8));
        Assert.Equal(accepted ? NeedleCompilationOutcome.Success : NeedleCompilationOutcome.Failed, result.GetOutcome());
        Assert.Equal(accepted ? 1 : 0, result.Calls.Count);
        Assert.Equal(accepted ? 0 : 1, result.RejectedCalls.Count);
    }

    [Fact]
    public void NativeSuppressionAndNegationFailClosed()
    {
        var suppressed = Parse("""
            {"type":"call","success":true,"confidence":0.9,"function_calls":[{"name":"set","arguments":{"mode":"eco","level":2}}],
             "suppressed_calls":[{"name":"set","arguments":{"mode":"boost","level":2}}],
             "validation":{"ungrounded":["set.mode"],"negation":false}}
            """);
        Assert.Single(suppressed.Calls);
        Assert.Single(suppressed.SuppressedCalls);
        Assert.Equal(NeedleCompilationOutcome.Suppressed, suppressed.GetOutcome());
        Assert.False(suppressed.IsConfident(0));
        var forwarded = JsonSerializer.Deserialize(JsonSerializer.Serialize(suppressed, NeedleJsonContext.Default.ToolCallCompilation),
            NeedleJsonContext.Default.ToolCallCompilation);
        Assert.Equal(NeedleCompilationOutcome.Suppressed, forwarded!.GetOutcome());
        Assert.Equal("set.mode", Assert.Single(forwarded.Validation!.Ungrounded!));

        var negated = Parse("""
            {"type":"call","success":true,"function_calls":[{"name":"set","arguments":{"mode":"eco","level":2}}],
             "suppressed_calls":[],"validation":{"ungrounded":[],"negation":true},"confidence":0.9}
            """);
        Assert.Empty(negated.Calls);
        Assert.Single(negated.RejectedCalls);
        Assert.Equal(NeedleCompilationOutcome.Suppressed, negated.GetOutcome());
    }

    [Fact]
    public void UnknownToolBlocksEntireBatchAndErrorMetadataSurvives()
    {
        var result = Parse("""
            {"type":"call","success":true,"function_calls":[{"name":"other","arguments":{}},{"name":"set","arguments":{"mode":"eco","level":2}}],
             "suppressed_calls":[],"confidence":0.9}
            """);
        Assert.Single(result.Calls);
        Assert.Equal("other", Assert.Single(result.RejectedCalls).Name);
        Assert.Equal(NeedleCompilationOutcome.Failed, result.GetOutcome());
        var error = Parse("""{"type":"call","success":false,"error":"token limit","error_code":"limit","reason":"test"}""");
        Assert.Equal("limit", error.ErrorCode);
        Assert.Equal("test", error.Reason);
        Assert.Equal(NeedleCompilationOutcome.Failed, error.GetOutcome());
    }

    [Theory]
    [InlineData("""{"type":"call","function_calls":[],"suppressed_calls":[]}""")]
    [InlineData("""{"type":"call","success":true,"function_calls":[]}""")]
    [InlineData("""{"type":"call","success":true,"function_calls":[],"suppressed_calls":[],"confidence":1.1}""")]
    [InlineData("""{"type":"call","success":true,"function_calls":[{"name":"set"}],"suppressed_calls":[]}""")]
    [InlineData("""{"type":"call","success":true,"function_calls":[],"suppressed_calls":[],"validation":{"negation":true}}""")]
    [InlineData("""{"type":"call","success":true,"function_calls":[],"suppressed_calls":[],"suppressed_calls":[]}""")]
    public void RejectsIncompleteOrContradictoryEnvelopes(string json) =>
        Assert.Throws<NeedleProtocolException>(() => Parse(json));

    private static ToolCallCompilation Parse(string json) => NeedleProtocol.Parse(Encoding.UTF8.GetBytes(json), [Tool]);
}
