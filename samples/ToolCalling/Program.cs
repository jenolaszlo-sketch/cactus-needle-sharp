using CactusNeedleSharp;

await using var needle = await NeedleClient.CreateAsync();
var tool = NeedleTool.FromJson("""
{"name":"search_repository","description":"Search source code in a repository","parameters":{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}}
""");
var result = await needle.CompileAsync("Search the repository for WorkflowEngine usages", [tool]);
Console.WriteLine($"Success: {result.Success}; confidence: {result.Confidence}");
foreach (var call in result.Calls) Console.WriteLine($"{call.Name}: {call.Arguments}");
