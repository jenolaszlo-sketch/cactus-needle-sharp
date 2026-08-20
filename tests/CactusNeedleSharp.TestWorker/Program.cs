using System.Text.Json;
using CactusNeedleSharp;

while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
{
    var request = JsonSerializer.Deserialize<WorkerRequest>(line, NeedleProtocol.Json)!;
    if (request.Operation == "shutdown") return 0;
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
