using System.Text.Json;
using System.Text.Json.Serialization;

namespace CactusNeedleSharp;

internal static class NeedleProtocol
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    internal static string SerializeTools(IReadOnlyList<NeedleTool> tools) =>
        JsonSerializer.Serialize(tools.ToList(), NeedleJsonContext.Default.ListNeedleTool);

    internal static ToolCallCompilation Parse(ReadOnlySpan<byte> utf8, IReadOnlyList<NeedleTool> tools)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            if (!NeedleArgumentSchema.UniqueProperties(document.RootElement))
                throw new NeedleProtocolException("Cactus returned a response with duplicate JSON properties.");
            var response = document.RootElement.Deserialize(NeedleJsonContext.Default.Response)
                ?? throw new NeedleProtocolException("Cactus returned an empty response.");
            if (string.IsNullOrWhiteSpace(response.Type) ||
                (response.Success && (response.FunctionCalls is null || response.SuppressedCalls is null || response.Error is not null)) ||
                (!response.Success && string.IsNullOrWhiteSpace(response.Error) && string.IsNullOrWhiteSpace(response.ErrorCode)))
                throw new NeedleProtocolException("Cactus returned an incomplete or inconsistent response envelope.");
            if (response.Confidence is { } confidence && (!double.IsFinite(confidence) || confidence is < 0 or > 1))
                throw new NeedleProtocolException("Cactus returned confidence outside the interval [0, 1].");
            if (!MetricValid(response.PrefillTps) || !MetricValid(response.DecodeTps) || !MetricValid(response.PeakRam))
                throw new NeedleProtocolException("Cactus returned an invalid inference metric.");
            if (response.Validation is { } validation &&
                (validation.Negation is null || validation.Ungrounded is null ||
                 validation.Ungrounded.Any(string.IsNullOrWhiteSpace)))
                throw new NeedleProtocolException("Cactus returned incomplete validation metadata.");

            var calls = ReadCalls(response.FunctionCalls);
            var suppressed = ReadCalls(response.SuppressedCalls);
            var rejected = new List<NeedleToolCall>();
            var accepted = new List<NeedleToolCall>();
            var withheld = response.Validation?.Negation == true ||
                (response.Validation?.Ungrounded?.Length > 0 && suppressed.Length == 0) ||
                !string.Equals(response.Type, "call", StringComparison.Ordinal);
            foreach (var call in calls)
            {
                var tool = tools.FirstOrDefault(tool => tool.Name == call.Name);
                if (!response.Success || withheld || tool is null || !NeedleArgumentSchema.Matches(call.Arguments, tool.Parameters))
                    rejected.Add(call);
                else
                    accepted.Add(call);
            }

            return new()
            {
                Success = response.Success,
                ResponseType = response.Type,
                Calls = accepted.ToArray(),
                SuppressedCalls = suppressed,
                RejectedCalls = rejected.ToArray(),
                Validation = response.Validation,
                Confidence = response.Confidence,
                Reason = response.Reason,
                Reasoning = response.Reasoning,
                Error = response.Error,
                ErrorCode = response.ErrorCode,
                PrefillTokensPerSecond = response.PrefillTps,
                DecodeTokensPerSecond = response.DecodeTps,
                PeakRamMegabytes = response.PeakRam
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { throw new NeedleProtocolException("Cactus returned malformed JSON.", exception); }
    }

    private static bool MetricValid(double? value) => value is null || (double.IsFinite(value.Value) && value >= 0);

    private static NeedleToolCall[] ReadCalls(Call?[]? values)
    {
        if (values is null) return [];
        var calls = new NeedleToolCall[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var call = values[index];
            if (call is null || string.IsNullOrWhiteSpace(call.Name) || call.Arguments.ValueKind != JsonValueKind.Object)
                throw new NeedleProtocolException("Cactus returned a call without a name or object arguments.");
            calls[index] = new() { Name = call.Name, Arguments = call.Arguments };
        }
        return calls;
    }

    internal sealed record Response
    {
        [JsonRequired] public string? Type { get; init; }
        [JsonRequired] public bool Success { get; init; }
        [JsonPropertyName("function_calls")] public Call?[]? FunctionCalls { get; init; }
        [JsonPropertyName("suppressed_calls")] public Call?[]? SuppressedCalls { get; init; }
        public NeedleNativeValidation? Validation { get; init; }
        public double? Confidence { get; init; }
        public string? Reason { get; init; }
        public string? Reasoning { get; init; }
        public string? Error { get; init; }
        [JsonPropertyName("error_code")] public string? ErrorCode { get; init; }
        [JsonPropertyName("prefill_tps")] public double? PrefillTps { get; init; }
        [JsonPropertyName("decode_tps")] public double? DecodeTps { get; init; }
        [JsonPropertyName("peak_ram_mb")] public double? PeakRam { get; init; }
    }

    internal sealed record Call
    {
        public string? Name { get; init; }
        public JsonElement Arguments { get; init; }
    }
}
