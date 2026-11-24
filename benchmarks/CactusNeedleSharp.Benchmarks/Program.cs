using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CactusNeedleSharp;

if (args is ["--native-comparison", var runtimePath, var workerPath, ..] && args.Length is 3 or 4)
    await NativeComparison.RunAsync(runtimePath, workerPath, args.Length == 4 ? args[3] : null);
else
    BenchmarkRunner.Run<ResponseDeserializationBenchmark>();

[MemoryDiagnoser]
public class ResponseDeserializationBenchmark
{
    private readonly string _toolJson = """{"name":"search_repository","description":"Search source code","parameters":{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}}""";
    [Benchmark] public NeedleTool DeserializeTool() => NeedleTool.FromJson(_toolJson);
}
