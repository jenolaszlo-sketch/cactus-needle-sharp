// NativeAOT smoke test: exercises the trim-sensitive JSON surface (schema
// generation from metadata, tool serialization, argument round-trips) without
// a native runtime. Any failure throws, so a zero exit code means the
// AOT-published binary preserved every path.
using System.Text.Json;
using System.Text.Json.Serialization;
using CactusNeedleSharp;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"AOT smoke check failed: {message}");
    }
}

var tool = NeedleTool.FromType("get_weather", AotSmokeContext.Default.WeatherArguments, "Get the weather.");
Check(tool.Parameters.GetProperty("type").GetString() == "object", "tool schema is an object");
Check(tool.Parameters.GetProperty("properties").TryGetProperty("city", out _), "tool schema has city");
Check(
    tool.Parameters.GetProperty("required").EnumerateArray().Any(x => x.GetString() == "city"),
    "tool schema requires city");

var call = new NeedleToolCall
{
    Name = "get_weather",
    Arguments = JsonSerializer.SerializeToElement(
        new WeatherArguments { City = "Budapest" }, AotSmokeContext.Default.WeatherArguments)
};
var arguments = call.DeserializeArguments(AotSmokeContext.Default.WeatherArguments);
Check(arguments.City == "Budapest", "typed arguments round-trip");
Check(
    call.TryDeserializeArguments(out WeatherArguments? retried, out var error, AotSmokeContext.Default.WeatherArguments) &&
    error is null &&
    retried == arguments,
    "try-deserialize round-trips");

var typed = NeedleTool.FromType("get_weather", AotSmokeContext.Default.WeatherArguments);
Check(
    typed.DeserializeCallArguments(call, AotSmokeContext.Default.WeatherArguments) == arguments,
    "tool-bound deserialization round-trips");

Console.WriteLine("CactusNeedleSharp AOT smoke test passed.");

internal sealed record WeatherArguments
{
    public required string City { get; init; }
    public string? Country { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WeatherArguments))]
internal sealed partial class AotSmokeContext : JsonSerializerContext
{
}
