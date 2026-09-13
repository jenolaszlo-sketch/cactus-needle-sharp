using System.Text.Json;

namespace CactusNeedleSharp.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void NativeTextRejectsNulAndInvalidUtf16()
    {
        Assert.Throws<ArgumentException>(() => NeedleValidation.NativeText("bad\0text", "value"));
        Assert.Throws<ArgumentException>(() => NeedleValidation.NativeText("\ud800", "value"));
        NeedleValidation.NativeText("valid π", "value");
    }

    [Fact]
    public void CompilationTokenOverrideMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NeedleValidation.CompilationOptions(new() { MaxNewTokens = 0 }));
        NeedleValidation.CompilationOptions(new() { MaxNewTokens = 1 });
    }

    [Fact]
    public void ToolAdmissionRejectsDuplicatesAndDetachesSchemas()
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{"q":{"type":"string"}}}""");
        var tool = new NeedleTool { Name = "search", Parameters = document.RootElement };

        Assert.Throws<NeedleSchemaException>(() => NeedleValidation.Tools([tool, tool]));
        var snapshot = NeedleValidation.Tools([tool]);
        document.Dispose();

        Assert.Equal("string", snapshot[0].Parameters.GetProperty("properties").GetProperty("q").GetProperty("type").GetString());
        Assert.Throws<NeedleSchemaException>(() => NeedleValidation.Tools([
            new NeedleTool { Name = " ", Parameters = JsonSerializer.SerializeToElement(new { }) }
        ]));
    }
}
