# CactusNeedleSharp

Unofficial .NET wrapper for Needle 2 by Cactus Compute.

CactusNeedleSharp exposes Needle's local, schema-constrained tool calling and structured extraction capabilities to .NET applications.

Needle 2 is developed by Cactus Compute. This project is an independent community integration and is not affiliated with, sponsored by, or endorsed by Cactus Compute.

```text
User / LLM intent
       │
       ▼
CactusNeedleSharp
       ├─ tool selection
       ├─ argument extraction
       ├─ schema-constrained decoding
       └─ confidence
       ▼
Typed ToolCall
```

Needle decides how an intent maps to a declared operation. Your application decides whether the operation is permitted and whether it should be executed.

Packages:

- `CactusNeedleSharp` — managed API, native interop, artifact management, and worker-pool client.
- `CactusNeedleSharp.Worker` — optional local .NET tool used for concurrent conversation isolation.
- `CactusNeedleSharp.Baize` — optional conversation-oriented adapter contracts for using the worker pool as Baize's tool planner.

## Quick start

```csharp
using CactusNeedleSharp;

await using var needle = await NeedleClient.CreateAsync();
var weather = NeedleTool.FromJson("""
{
  "name": "get_weather",
  "description": "Get the current weather",
  "parameters": {
    "type": "object",
    "properties": { "city": { "type": "string" } },
    "required": ["city"]
  }
}
""");

var result = await needle.CompileAsync("What's the weather in Budapest?", [weather]);
if (result.IsConfident(0.80))
    foreach (var call in result.Calls)
        Console.WriteLine($"{call.Name}: {call.Arguments}");
```

The first call downloads the third-party Needle 2 platform runtime from the official Cactus Compute distribution into a separate local cache. The runtime wheel size and SHA-256 are pinned, and a manifest protects subsequent offline reuse. The runtime is not embedded in the NuGet package. Set `Offline = true` after the artifact is present, or provide `NativeLibraryPath`/upstream `NEEDLE_LIB_PATH` for air-gapped use. No telemetry is collected.

Model-dependent tests are isolated in `CactusNeedleSharp.IntegrationTests` and run only when `NEEDLE_RUN_INTEGRATION_TESTS=1`; ordinary unit tests never download artifacts. BenchmarkDotNet benchmarks are kept separate from tests so cold initialization is not reported as warm inference throughput.

Release packaging is audited in CI: the package license, README, license and notice files are verified, and packaging fails if model weights or native Needle runtime binaries enter the NuGet archive unexpectedly.

## Sessions and extraction

```csharp
await using var session = await needle.CreateAsync([weather]);
var first = await session.CompleteAsync("Weather in Budapest");
var next = await session.CompleteAsync("{\"temperature_c\": 22}");
await session.ResetAsync();

var invoice = await needle.ExtractAsync<Invoice>("Invoice from Acme Corp for $1,200.");
record Invoice(string Vendor, decimal Total);
```

Needle's confirmed ABI is process-global, so the in-process transport allows one live session per process and serializes calls within it. See [the runtime investigation](docs/runtime-investigation.md).

For concurrent, conversation-isolated workloads, use the bounded worker pool. Each active conversation exclusively leases one Needle child process; healthy base-model workers are reset and reused:

```csharp
using CactusNeedleSharp.Worker;

await using var pool = new NeedleWorkerPool(new()
{
    MaximumWorkers = 4,
    MaximumQueueLength = 100,
    QueueTimeout = TimeSpan.FromSeconds(10)
});

await pool.WarmAsync(2);

await using var conversation = await pool.CreateSessionAsync(tools);
var decision = await conversation.CompleteAsync(userIntent);
```

Worker auto-discovery checks the application directory; `WorkerPath` remains available for explicit deployment and local-tool configurations. Reference the `CactusNeedleSharp.Worker` project, deploy its build output alongside the application, or install the `CactusNeedleSharp.Worker` local .NET tool. See [conversation-isolated worker pools](docs/worker-pool.md).

## Dependency injection and typed outcomes

```csharp
services.AddCactusNeedleSharp(new NeedleOptions { Offline = true });
services.AddCactusNeedleSharpWorkerPool(new() { MaximumWorkers = 4 });

var outcome = result.GetOutcome(new() { MinimumConfidence = .80 });
var arguments = result.Calls[0].DeserializeArguments<WeatherArguments>();
```

Outcomes distinguish `Success`, `NoCall`, `LowConfidence`, and `Failed`; typed argument deserialization reports a `NeedleProtocolException` instead of leaking raw JSON errors.

## Baize adapter

`CactusNeedleSharp.Baize` keeps one leased worker session per conversation and converts portable Baize tool definitions into Needle schemas. It only plans calls and never executes them. Keeping it separate lets Baize select this planner, LLamaSharp, or another `IToolCallPlanner` implementation without coupling the core package.

## Add tools to a text-only model

```text
Text-only / weak-tool LLM
          │ natural-language intent
          ▼
 CactusNeedleSharp
          │ schema-constrained call
          ▼
      Tool executor
```

Needle allows tool selection and schema-constrained argument generation to be separated from the reasoning model. It does not make every model a reliable autonomous agent.

## Security

The package never executes generated calls. **Schema validity != semantic correctness.** Apply authorization, policy checks, business/domain validation, confidence thresholds, and confirmation for destructive actions before execution.

## Upstream project and attribution

Needle 2 is developed by the Cactus Compute team.

CactusNeedleSharp only provides the .NET integration layer. It does not claim authorship of the Needle model, model architecture, training work, model weights, or Cactus runtime.

Please visit the upstream distributions for the authoritative documentation, models, licenses, research, authors, and academic citation:

- [Cactus Compute Needle](https://github.com/cactus-compute/needle)
- [Needle 2 model distribution](https://huggingface.co/Cactus-Compute/needle2)

## License

CactusNeedleSharp source code is licensed under the [Apache License 2.0](LICENSE).

Needle 2, Cactus runtime components, model weights, and other upstream Cactus Compute artifacts retain their respective upstream licenses and copyright notices.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the official Cactus Compute distributions for details.

## Disclaimer

CactusNeedleSharp is an independent open-source project.

It is not an official Cactus Compute product and is not affiliated with, sponsored by, or endorsed by Cactus Compute.
