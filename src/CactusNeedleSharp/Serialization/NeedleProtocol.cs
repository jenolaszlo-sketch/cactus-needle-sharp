using System.Text.Json;
using System.Text.Json.Serialization;

namespace CactusNeedleSharp;

internal static class NeedleProtocol
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    internal static string SerializeTools(IReadOnlyList<NeedleTool> tools) =>
        JsonSerializer.Serialize(tools.ToList(), NeedleJsonContext.Default.ListNeedleTool);

    internal static ToolCallCompilation Parse(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var response = JsonSerializer.Deserialize(utf8, NeedleJsonContext.Default.Response) ?? throw new NeedleProtocolException("Needle returned an empty response.");
            return new()
            {
                Success = response.Success,
                Calls = response.FunctionCalls?.Select(c => new NeedleToolCall { Name = c.Name ?? string.Empty, Arguments = c.Arguments.Clone() }).ToArray() ?? [],
                Confidence = response.Confidence,
                Reasoning = response.Reasoning,
                Error = response.Error,
                ErrorCode = response.ErrorCode,
                PrefillTokensPerSecond = response.PrefillTps,
                DecodeTokensPerSecond = response.DecodeTps
            };
        }
        catch (NeedleProtocolException) { throw; }
        catch (JsonException exception) { throw new NeedleProtocolException("Needle returned malformed JSON.", exception); }
    }

    internal sealed record Response
    {
        public bool Success { get; init; }
        [JsonPropertyName("function_calls")] public Call[]? FunctionCalls { get; init; }
        public double? Confidence { get; init; }
        public string? Reasoning { get; init; }
        public string? Error { get; init; }
        [JsonPropertyName("error_code")] public string? ErrorCode { get; init; }
        [JsonPropertyName("prefill_tps")] public double? PrefillTps { get; init; }
        [JsonPropertyName("decode_tps")] public double? DecodeTps { get; init; }
    }
    internal sealed record Call { public string? Name { get; init; } public JsonElement Arguments { get; init; } }
}
