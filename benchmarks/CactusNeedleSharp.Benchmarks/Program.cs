using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CactusNeedleSharp;

BenchmarkRunner.Run<ResponseDeserializationBenchmark>();

[MemoryDiagnoser]
public class ResponseDeserializationBenchmark
{
    private readonly string _toolJson = """{"name":"search_repository","description":"Search source code","parameters":{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}}""";
    [Benchmark] public NeedleTool DeserializeTool() => NeedleTool.FromJson(_toolJson);
}
