using System.Text.Json;

namespace CactusNeedleSharp;

internal static class NeedleExtractionResults
{
    internal static NeedleExtractionResult<T> Create<T>(ToolCallCompilation compilation,
        string expectedToolName, Func<JsonElement, T?> deserialize, string typeName)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(deserialize);
        if (!compilation.Success)
            return new() { Success = false, Confidence = compilation.Confidence, Error = compilation.Error, Compilation = compilation };
        if (compilation.Calls.Count != 1 || !string.Equals(compilation.Calls[0].Name, expectedToolName, StringComparison.Ordinal))
            return new() { Success = false, Confidence = compilation.Confidence, Error = "Needle did not return exactly one extraction call.", Compilation = compilation };
        try
        {
            var value = deserialize(compilation.Calls[0].Arguments);
            return value is null
                ? new() { Success = false, Confidence = compilation.Confidence, Error = "Needle returned a null extraction value.", Compilation = compilation }
                : new() { Success = true, Value = value, Confidence = compilation.Confidence, Compilation = compilation };
        }
        catch (JsonException exception)
        {
            throw new NeedleProtocolException($"Needle output could not be deserialized as {typeName}.", exception);
        }
    }
}
