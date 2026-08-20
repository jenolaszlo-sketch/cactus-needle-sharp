# Conversation-isolated worker pool

Needle's confirmed native ABI stores model and conversation state globally and exposes no session handle. `NeedleWorkerPool` provides concurrent, isolated sessions by leasing one dedicated child process to each active conversation.

```text
Baize / application
        │
        ├── lease ── worker A ── conversation A
        ├── lease ── worker B ── conversation B
        └── wait  ── bounded backpressure
```

The worker executable is framework-dependent and must be deployed with the application. Pass its executable or `.dll` path through `NeedleWorkerPoolOptions.WorkerPath`. A project reference to `CactusNeedleSharp.Worker` copies its executable assets to the application output; `typeof(NeedleWorkerMarker).Assembly.Location` locates the copied DLL.

The worker is also packaged as the `CactusNeedleSharp.Worker` .NET tool. With a local tool manifest, configure the pool to launch it through `dotnet tool run`:

```text
dotnet new tool-manifest
dotnet tool install CactusNeedleSharp.Worker
```

```csharp
new NeedleWorkerPoolOptions
{
    WorkerPath = "dotnet",
    WorkerArguments = ["tool", "run", "cactusneedlesharp-worker"],
    MaximumWorkers = 4
};
```

Each worker communicates using correlation-checked newline-delimited JSON over redirected standard streams. A versioned capability handshake rejects incompatible core/worker combinations. Protocol responses carry a private prefix so incidental native stdout cannot be mistaken for a response; diagnostics are drained separately. Requests within one session are serialized and bounded by `MaximumProtocolMessageLength`.

`MaximumWorkers` bounds native processes and active conversations. A session waiting beyond the bound observes its cancellation token. Disposing a base-model session closes its native session and returns a healthy process to the pool. A custom-weight worker is terminated instead because the upstream engine cannot unload weights safely. Timed-out or protocol-failed workers are terminated and are never returned to the pool.

The artifact cache uses both an in-process gate and a cross-process file lock, so workers can share a cache without racing its initial installation. Each worker still loads its own copy of the native model into memory.

Choose `MaximumWorkers` from measured memory and latency rather than CPU count alone. The default is capped at four, but embedded devices and custom weights may require a smaller limit.

`MaximumQueueLength` and `QueueTimeout` bound backpressure. `IdleWorkerTimeout` retires stale workers when they are next leased, `WarmAsync` prestarts a chosen number of processes, and `AdmissionCheck` can reject new processes under application-specific memory pressure. Pool counters expose active, idle, and waiting counts; OpenTelemetry-compatible meters report queue duration and worker start, reuse, discard, and failure events.

Session disposal waits for an in-flight operation before returning its worker and is idempotent under concurrent calls. Pool disposal cancels queued acquisitions and terminates all workers, including workers with outstanding leases. Applications should normally dispose conversation sessions before disposing the pool.
