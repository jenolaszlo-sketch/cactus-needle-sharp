using System.Text.Json;
using CactusNeedleSharp;

NeedleTool[] tools = [];
while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
{
    var request = JsonSerializer.Deserialize<WorkerRequest>(line, NeedleProtocol.Json)!;
    if (request.Operation == "shutdown") return 0;
    if (request.Operation == "initialize")
        tools = request.Payload!.Value.Deserialize<WorkerInitializePayload>(NeedleProtocol.Json)!.Tools;
    if (request.Operation == "complete")
    {
        var completion = request.Payload!.Value.Deserialize<WorkerCompletePayload>(NeedleProtocol.Json)!;
        if (completion.Input == "crash") Environment.Exit(17);
        if (completion.Input == "delay") await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (completion.Input == "wrong-correlation") request = request with { Id = "wrong" };
    }

    object payload = request.Operation switch
    {
        "ping" => new WorkerHandshake
        {
            ProtocolVersion = WorkerProtocol.Version,
            WorkerVersion = "test",
            RuntimeFramework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Capabilities = ["test"]
        },
        "complete" when request.Payload!.Value.Deserialize<WorkerCompletePayload>(NeedleProtocol.Json)!.Input == "native-failure" =>
            NeedleProtocol.Parse(JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "call", success = true, error_code = "native_failure", confidence = 1,
                function_calls = new[] { new { name = tools[0].Name, arguments = new { value = 1 } } },
                suppressed_calls = Array.Empty<object>()
            }), tools),
        "complete" => new ToolCallCompilation
        {
            Success = true,
            Calls = [new NeedleToolCall { Name = "test", Arguments = JsonSerializer.SerializeToElement(new { value = 1 }) }],
            Confidence = 1,
            Reasoning = request.Payload!.Value.Deserialize<WorkerCompletePayload>(NeedleProtocol.Json)!.Input == "oversize"
                ? new string('x', 4096)
                : null
        },
        _ => new { ok = true }
    };
    var response = new WorkerResponse
    {
        Id = request.Id,
        Success = true,
        Payload = JsonSerializer.SerializeToElement(payload, NeedleProtocol.Json)
    };
    await Console.Out.WriteLineAsync(WorkerProtocol.Prefix + JsonSerializer.Serialize(response, NeedleProtocol.Json)).ConfigureAwait(false);
    await Console.Out.FlushAsync().ConfigureAwait(false);
}

return 0;
