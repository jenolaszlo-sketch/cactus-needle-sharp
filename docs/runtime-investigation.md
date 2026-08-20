# Runtime investigation

Investigated 2026-08-21 against the upstream `main` branches and the `Cactus-Compute/needle2` Hugging Face repository. Upstream is moving quickly; artifact version 2.0.3 is pinned by this wrapper and must be re-verified before each release.

The declarations in `NeedleNative.cs` were independently authored from the public signatures below; no upstream implementation source was copied. The authoritative ABI source inspected was the official platform `needle.h` in the Needle 2 distribution.

## Findings

Needle 2 has a dedicated, process-global C ABI declared by the official `needle.h`:

```c
int needle_init(const char *system_prompt, const char *tools_json, const char *tool_index_path);
int needle_complete(const char *input, int max_new_tokens, char *out, int out_capacity);
void needle_reset(void);
int needle_load(const unsigned char *cact, unsigned long long n);
```

`needle_init` and `needle_complete` use negative return values for failure; non-negative values indicate success. The response JSON is read from the NUL-terminated output buffer (the Python binding intentionally does not treat the non-negative completion return as a byte count).

The official Python package resolves `NEEDLE_LIB_PATH` first and otherwise downloads a platform-specific wheel from `Cactus-Compute/needle2/python`. Those wheels contain `libneedle.dll`, `libneedle.so`, or `libneedle.dylib`. Engine version 2.0.3 is declared in upstream `needle/agent/fetch.py` at the investigated revision.

The deploy folders contain a CLI and `libneedle.a`, while the Python wheels carry the dynamic libraries required by .NET. The primary NuGet package therefore downloads the official wheel on first use and extracts only its dynamic library into an atomic local cache. It never bundles upstream runtime/model artifacts.

## ABI and session semantics

The ABI is global: it exposes no model/session handle. `needle_init` binds one toolset, repeated `needle_complete` calls retain state, and `needle_reset` clears that state while preserving tools. Custom `.cact` bytes are loaded using `needle_load`.

Consequently, v0.1 permits one live `INeedleSession` per process and holds a process-wide lease for its lifetime. Calls within a session are also single-flight. This is an explicit upstream constraint, not a presumed thread-safety property. Separate processes are needed for truly concurrent independent sessions or different custom/base weights.

Cancellation works before native entry and prevents managed response processing afterward. The confirmed ABI exposes no stop function, so native compute may finish internally after cancellation is requested.

## Not the generic Cactus Engine ABI

The generic Cactus Engine exports handle-based `cactus_init`, `cactus_complete`, `cactus_stop`, and `cactus_destroy`. Needle 2 instead ships a baked, specialist engine and its own four-function ABI. No upstream source or artifact establishes that `needle2.cact` can be loaded by generic `cactus_init`, nor that generic `cactus_complete` preserves Needle's retrieval head, grammar compiler, calibrated confidence head, or global session semantics. This wrapper therefore does not bind the generic Cactus Engine for Needle inference.

## Platforms

Official deploy folders exist for Windows x64/ARM64, Linux x64/ARM64 (plus ARMv7, RISC-V, and MIPS), macOS ARM64, Android, Apple mobile platforms, and WASM. Official Python wheels provide dynamic libraries for Windows x64/ARM64, Linux glibc x64/ARM64 (and additional upstream variants), and macOS ARM64. This wrapper initially resolves Windows x64/ARM64, Linux glibc x64/ARM64, and macOS ARM64/x64 wheel tags; only upstream-published combinations can download successfully.

## Sources

- https://github.com/cactus-compute/needle/blob/main/needle/agent/fetch.py
- https://github.com/cactus-compute/needle/blob/main/doc/apis.md
- https://huggingface.co/Cactus-Compute/needle2/blob/main/windows-x86_64/needle.h
- https://huggingface.co/Cactus-Compute/needle2
- https://github.com/cactus-compute/cactus/blob/main/docs/cactus_engine.md
