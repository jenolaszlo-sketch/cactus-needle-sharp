# CactusNeedleSharp

Unofficial .NET wrapper for Needle 3 by Cactus Compute.

CactusNeedleSharp exposes Needle's local, schema-constrained tool calling and structured extraction capabilities to .NET applications.

Needle 3 is developed by Cactus Compute. This project is an independent community integration and is not affiliated with, sponsored by, or endorsed by Cactus Compute.

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

The default client downloads the pinned Cactus 4.1.4 shared library and Needle 3 weights into separate verified cache entries. Once both are present, set `Offline = true` for disconnected use. Air-gapped deployments can set `NativeLibraryPath` and `ModelPath` explicitly. See [native runtime and migration](docs/native-runtime.md) for integrity and version reporting.

Native execution tests are opt-in. See [integration testing](docs/integration-testing.md) and the [qualification report](docs/needle3-qualification.md) for run instructions, platform evidence, and the Needle 2 comparison.

Release packaging is audited in CI: the package license, README, license and notice files are verified, and packaging fails if model weights or native Needle runtime binaries enter the NuGet archive unexpectedly.

## Independent benchmark

Release Q1 reran both versions on the same Windows x64 host.

| Metric | Needle 2 | Needle 3 |
|---|---:|---:|
| Median warm inference | 181 ms | 297 ms |
| Direct working set | 59 MiB | 135 MiB |
| Correct proposals accepted at 0.80 | 20 | 15 |
| False positives accepted at 0.80 | 5 | 0 |

The [independent Q1 report](docs/reviews/cactus-needle-sharp-n3-release-q1.md#independent-benchmark) gives the workload and labels the earlier producer run separately.

## Sessions and extraction

```csharp
await using var session = await needle.CreateSessionAsync([weather]);
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
await conversation.DisposeAsync();

// The pool implements the same one-shot contracts as NeedleClient.
IToolCallCompiler isolated = pool;
var oneShot = await isolated.CompileAsync(userIntent, tools);
```

Worker auto-discovery checks the application directory; `WorkerPath` remains available for explicit deployment and local-tool configurations. Reference the `CactusNeedleSharp.Worker` project, deploy its build output alongside the application, or install the `CactusNeedleSharp.Worker` local .NET tool. See [conversation-isolated worker pools](docs/worker-pool.md).

## Dependency injection and typed outcomes

```csharp
services.AddCactusNeedleSharp(new NeedleOptions { Offline = true });
services.AddCactusNeedleSharpWorkerPool(new() { MaximumWorkers = 4 });

var outcome = result.GetOutcome(new() { MinimumConfidence = .80 });
if (outcome == NeedleCompilationOutcome.Success)
{
    var arguments = result.Calls[0].DeserializeArguments<WeatherArguments>();
    Console.WriteLine(arguments);
}
```

Outcomes distinguish `Success`, `NoCall`, `LowConfidence`, `Suppressed`, and `Failed`. Read intent candidates through `Calls` only after a successful outcome. `SuppressedCalls`, `RejectedCalls`, and `Validation` carry diagnostic information.

`IsConfident` and `GetOutcome` apply the same threshold rule: a missing model confidence is treated as low confidence, and thresholds must be finite values from `0` through `1`. Per-call `MaxNewTokens` overrides must be positive. Typed tools retain the serializer contract used to generate their schema, and calls are checked against the typed tool name before deserialization.

Generated schemas follow the selected `System.Text.Json` contract, including resolved property names, ignored members, enum representation, and string-keyed dictionaries as JSON objects. Typed extraction also accepts `JsonTypeInfo<T>` through `IStructuredExtractor`; set `NeedleExtractionOptions.NestedTypeResolver` when that schema references other source-generated types. Tools and native text are validated before a request crosses the native or worker boundary; duplicate tool names and embedded NUL characters are rejected.

Worker pools apply admission checks to both demand-driven creation and `WarmAsync`. Pool disposal waits for an in-progress worker start and rejects a worker that loses the shutdown race, while failed initialization returns its capacity exactly once. The pool uses a bounded queue and lazily expires idle workers when they are checked out.

The .NET wrapper supports typed extraction through its own schema verifier. Upstream Python adds date grounding, repair, and extraction rules described in the [runtime investigation](docs/runtime-investigation.md).

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

Needle 3 is developed by the Cactus Compute team.

CactusNeedleSharp only provides the .NET integration layer. It does not claim authorship of the Needle model, model architecture, training work, model weights, or Cactus runtime.

Please visit the upstream distributions for the authoritative documentation, models, licenses, research, authors, and academic citation:

- [Cactus Compute Needle](https://github.com/cactus-compute/cactus/tree/v4.2.7)
- [Needle 3 model distribution](https://huggingface.co/Cactus-Compute/needle3)

## License

CactusNeedleSharp source code is licensed under the [Apache License 2.0](LICENSE).

Needle 3, Cactus runtime components, model weights, and other upstream Cactus Compute artifacts retain their respective upstream licenses and copyright notices.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the official Cactus Compute distributions for details.

## Disclaimer

CactusNeedleSharp is an independent open-source project.

It is not an official Cactus Compute product and is not affiliated with, sponsored by, or endorsed by Cactus Compute.
