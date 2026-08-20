# Architecture

`NeedleClient` is a tool-call compiler, session factory, and structured extractor. Tools remain canonical JSON Schema and arguments remain `JsonElement`. The core never executes generated calls.

`HuggingFaceNeedleArtifactProvider` resolves an explicit `NativeLibraryPath` or upstream `NEEDLE_LIB_PATH`, then an existing cache, and only then the official Hugging Face artifact. Offline mode disables download. `NeedleModelManager` keeps artifact management separate from inference.

`NeedleNative` is the narrow ABI compatibility layer. `NeedleSession` owns the process-global runtime lease and serializes its own inference. Public contracts contain no pointers or ABI details, allowing a future worker-process transport without public API changes.

For concurrent applications, `NeedleWorkerPool` leases a dedicated `CactusNeedleSharp.Worker` process to each conversation. Base-model workers are reset and reused; custom-weight or unhealthy workers are terminated. A versioned handshake, bounded frames, queue limits, admission control, idle expiry, and idempotent lifecycle state protect the process boundary. See `worker-pool.md`.

`IToolCallPlanner` is the backend-neutral planning seam. `CactusNeedleSharp.Baize` adapts portable Baize tool definitions to conversation-isolated Needle sessions without putting Baize policy or tool execution into the core.

Schema validity is not semantic correctness. Applications must authorize, validate, and confirm calls before execution.
