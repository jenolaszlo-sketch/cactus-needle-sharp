using CactusNeedleSharp;

await using var needle = await NeedleClient.CreateAsync();
var tool = NeedleTool.FromJson("""
{"name":"search_repository","description":"Search source code in a repository","parameters":{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}}
""");
var result = await needle.CompileAsync("Search the repository for WorkflowEngine usages", [tool]);
var outcome = result.GetOutcome();
Console.WriteLine($"Outcome: {outcome}. Confidence: {result.Confidence}");
if (outcome == NeedleCompilationOutcome.Success)
    foreach (var call in result.Calls)
        Console.WriteLine($"{call.Name}: {call.Arguments}");
