using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CactusNeedleSharp;

/// <summary>
/// Ahead-of-time source-generated serializers for every JSON shape the wrapper
/// exchanges internally. Internal traffic never uses reflection serialization,
/// so trimmed and NativeAOT hosts keep working; only user-supplied generic
/// payloads may still require runtime metadata.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NeedleProtocol.Response))]
[JsonSerializable(typeof(NeedleProtocol.Call))]
[JsonSerializable(typeof(WorkerRequest))]
[JsonSerializable(typeof(WorkerResponse))]
[JsonSerializable(typeof(WorkerHandshake))]
[JsonSerializable(typeof(WorkerInitializePayload))]
[JsonSerializable(typeof(WorkerCompletePayload))]
[JsonSerializable(typeof(ArtifactManifest))]
[JsonSerializable(typeof(NeedleTool))]
[JsonSerializable(typeof(List<NeedleTool>))]
[JsonSerializable(typeof(ToolCallCompilation))]
[JsonSerializable(typeof(NeedleToolCall))]
[JsonSerializable(typeof(JsonObject))]
internal sealed partial class NeedleJsonContext : JsonSerializerContext
{
}
