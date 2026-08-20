using CactusNeedleSharp;
using CactusNeedleSharp.Worker;

var workerPath = typeof(NeedleWorkerMarker).Assembly.Location;
await using var pool = new NeedleWorkerPool(new()
{
    WorkerPath = workerPath,
    MaximumWorkers = 2
});

var search = NeedleTool.FromJson("""{"name":"search_repository","description":"Search source code","parameters":{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}}""");
var weather = NeedleTool.FromJson("""{"name":"get_weather","description":"Get weather for a city","parameters":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}}""");

await using var conversationA = await pool.CreateSessionAsync([search]);
await using var conversationB = await pool.CreateSessionAsync([weather]);
var results = await Task.WhenAll(
    conversationA.CompleteAsync("Search for WorkflowEngine usages").AsTask(),
    conversationB.CompleteAsync("What is the weather in Manila?").AsTask());

foreach (var result in results)
    foreach (var call in result.Calls)
        Console.WriteLine($"{call.Name}: {call.Arguments} (confidence {result.Confidence})");
