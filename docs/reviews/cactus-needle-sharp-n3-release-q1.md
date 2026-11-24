# CactusNeedleSharp 0.3.0 independent release qualification

**CACTUS-NEEDLE-SHARP-N3-RELEASE-Q1 · 2026-11-24 · REMEDIATION REQUIRED**

Hold public NuGet publication. The candidate built and ran on three x64 targets. The managed response boundary accepted a contradictory error envelope, one clean Release test run failed during worker startup, and the exact candidate has no hosted CI evidence.

## Repository and artifact identity

| Item | Verified identity |
|---|---|
| Repository | `jenolaszlo-sketch/cactus-needle-sharp` |
| Candidate commit | `596b961727fc67a543799679453b777e49ddf3fc` |
| Candidate tree | `26b6c1d7d5391e16a6f0d010f8a5f56e60e4b4cf` |
| GitHub `main` and Needle 2 comparison commit | `1920988115203a3b7d048dac9d35e00d8f265f95` |
| Upstream client | `cactus-compute` 4.2.7 at `3b0898b99d2479a674a453827f01d3c20099896f` |
| Cactus engine | 4.1.4 at revision `f4ab2741203a282c6e7b89983de2b5e5b2e805a8` |
| Engine manifest SHA-256 | `ab948753f08e54d2b24dd04830dcb984885524ab3bd1e9da3bf207276ab62eb6` |
| Needle 3 model | `needle3.cact`, 35,335,380 bytes, revision `d6c7d6a2e9655505e0f8e23339356dd1705900eb` |
| Needle 3 model SHA-256 | `c9d915eca282ed42d1a09b143b592adb4cc6744ffe2d294adf5cfc5548170c38` |
| Needle 2.0.3 Windows benchmark wheel SHA-256 | `3c012603a6bc5d7f36aa26da3d0819a8fa226dd40c7f242013b5e214a51168c7` |

The canonical checkout was clean on local `main` before review. The origin URL matched the stated repository. A fresh GitHub query returned the older commit for `main`. It is an ancestor of the candidate, with zero remote-only and seven candidate-only commits. GitHub returned HTTP 422 for the candidate SHA and the Actions API returned zero runs for it. The prior delivery fast-forwarded the local checkout only. The baseline diff passed `git diff --check` and covered runtime behavior, protocol and lifecycle, tests, benchmarks, documentation, and metadata. The patched NativeAOT compiler pin serves release testing. [Remote main](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/commit/1920988115203a3b7d048dac9d35e00d8f265f95)

I downloaded the pinned manifest, ABI header, model, and all eight wheels. The manifest matched the tracked bytes. All wheels matched the upstream hashes and sizes. The extracted libraries matched the managed hash and size pins and the expected PE, ELF, or Mach-O architecture markers. The model tag was `0x05e12a84` with 581 tensor records. [Tagged source pins](https://github.com/cactus-compute/cactus/blob/3b0898b99d2479a674a453827f01d3c20099896f/cactus/agent/fetch.py) · [Pinned manifest](../cactus-native-4.1.4-manifest.json) · [Per-platform library identities](../needle3-qualification.md#artifact-evidence)

## Native ABI and lifecycle

The six required exports matched the pinned header and tagged upstream binding. The wrapper uses the C calling convention and UTF-8 strings. `cactus_load` takes bytes and a 64-bit unsigned length. Text completion passes null PCM and zero samples and provides a writable buffer with signed 32-bit capacity. A direct Windows ABI call returned 40 for 378 JSON bytes. A 16-byte output buffer returned −1 and a last-error message giving the required size. The loaded model remained usable after the caller released its buffer. [Pinned header](https://huggingface.co/Cactus-Compute/cactus/resolve/f4ab2741203a282c6e7b89983de2b5e5b2e805a8/releases/4.1.4/windows-x86_64/cactus.h) · [Tagged ABI binding](https://github.com/cactus-compute/cactus/blob/3b0898b99d2479a674a453827f01d3c20099896f/cactus/agent/runtime.py)

The core verifies exports, rehashes the engine before binding and the model bytes before native load, bounds-checks model records, and rejects the Needle 2 tag. A single lease protects model load, init, inference, and reset. The first loaded model identity stays resident. Model transitions use a fresh worker. Offline tests rejected tampered engine bytes and a truncated model. A distinct archive with unchanged tensor contents ran in a fresh worker and returned unknown calibration. Independently fine-tuned weights remain unqualified.

## Behavior and worker isolation

The native ABI probe produced an ordinary weather call, a suppressed missing-city proposal, and an empty translation proposal. The prohibited weather request still produced a native candidate at confidence 0.8404 with `validation.negation: true`. The managed acceptance checks withheld it. The live tests exercised typed extraction, Unicode, tool selection, multi-call proposals, argument constraints, reset, and conversation isolation.

| Independent parser probe | Candidate result |
|---|---|
| Schema-valid call at 0.8000 | `Success` |
| Schema-valid call at 0.7999 or with missing confidence | `LowConfidence` |
| Mixed valid/invalid arguments or an unknown tool | `Failed` |
| Negation or combined valid and suppressed calls | `Suppressed` |
| Duplicate argument key or success with non-null error text | `NeedleProtocolException` |
| Success with null error text and a non-null failure code | **`Success` and `IsConfident(0.80) == true`** |

The last result came from invoking the actual candidate parser with a matching thermostat schema and the envelope below. This was a synthetic fail-closed boundary check. The official binary's live inference responses in this qualification did not exhibit the contradiction.

```json
{"type":"call","success":true,"confidence":1,"error":null,"error_code":"native_failure","function_calls":[{"name":"thermostat","arguments":{"temperature":21}}],"suppressed_calls":[]}
```

The success guard checks `Error` while the failure guard checks both `Error` and `ErrorCode`. A contradictory code can therefore reach outcome classification as a confident proposal. Reject non-null failure codes on success and cover code-only and text-only contradictions in the regression suite. [Response consistency guard](../../src/CactusNeedleSharp/Serialization/NeedleProtocol.cs#L23)

The real 0.3.0 core rejected the 0.2.0 worker's protocol 1 response. The real 0.2.0 core rejected the 0.3.0 worker's protocol 2 response. Both pairs rejected during handshake and retained zero worker slots. Matching builds demonstrated bounded concurrency, queueing, reuse, reset, crash recovery, and capacity return. An independently installed 0.3.0 tool reported protocol 2 and accepted orderly shutdown. [Integration setup](../integration-testing.md)

## Independent execution matrix

| Platform | Artifact check | Q1 execution |
|---|---|---|
| Windows x64 | Passed | 8/8 native from an empty cache. Release build had zero warnings and errors |
| Windows ARM64 | Passed | Host execution pending |
| Linux glibc x64 | Passed | 8/8 fresh-cache and 8/8 loopback-only on Ubuntu 26.04.1 WSL2 and .NET 8.0.33 |
| Linux glibc ARM64 | Passed | Host execution pending |
| Linux musl x64 | Passed | 8/8 by direct invocation of the existing acceptance methods, including the real worker, in loopback-only Alpine 3.23.8 on .NET 8.0.33 |
| Linux musl ARM64 | Passed | Host execution pending |
| macOS x64 | Passed | Host execution pending |
| macOS ARM64 | Passed | Host execution pending |

The Windows solution test ran managed and native assemblies concurrently. The managed assembly passed 62/63. `DisposeWaitsForInFlightOperationAndIsIdempotent` failed during the initial worker ping before testing disposal. The unchanged managed project then passed 63/63 in isolation. Its 200 ms request timeout also bounds startup ping under the separately configured startup budget. Stabilize the test by synchronizing on completion entering flight and controlling cancellation after startup. Confirm the intended precedence of the two timeout settings. [Test setup](../../tests/CactusNeedleSharp.Tests/WorkerPoolTests.cs#L64) · [Startup handshake](../../src/CactusNeedleSharp/Workers/NeedleWorkerProcess.cs#L67) · [Request timeout](../../src/CactusNeedleSharp/Workers/NeedleWorkerProcess.cs#L122)

The glibc NativeAOT sample was rebuilt from the candidate for `linux-x64` with the pinned 8.0.33 compiler. It passed with external networking disabled and exercised real inference and source-generated typed extraction from the verified cache. Alpine used a direct runner over the exact eight acceptance methods. These are independent Q1 executions. The producer report is historical context.

## Independent benchmark

I built the exact Needle 2 baseline and exact candidate, using independently verified runtime files and the repository harness. The comparison ran sequentially on the same Intel Core Ultra 5 125H host with 18 logical processors, Windows build 26200, and .NET 8.0.31. Each transport warmed once and reset before each sample. The seven prompts were repeated five times per transport.

| Measure | Producer Needle 2 → Needle 3 | Q1 Needle 2 → Needle 3 |
|---|---:|---:|
| Direct warm median | 147.1 → 238.4 ms | 181.0 → 296.7 ms |
| Direct warm p95 | 250.7 → 346.4 ms | 306.6 → 382.1 ms |
| Direct working set | 59.25 → 134.92 MiB | 59.12 → 135.13 MiB |
| Cold first session | 271.7 → 360.9 ms | 401.3 → 433.3 ms |
| Direct median prefill | 563.2 → 413.3 tokens/s | 403.4 → 333.6 tokens/s |
| Direct median decode | 261.7 → 192.8 tokens/s | 181.8 → 160.9 tokens/s |
| Worker warm median | 139.6 → 248.3 ms | 177.7 → 293.6 ms |
| Reused worker session initialization | 1.3 → 47.8 ms | 1.3 → 53.7 ms |
| Correct tool and arguments | 20/20 → 20/20 | 20/20 → 20/20 |
| Correct proposals accepted at 0.80 | 20/20 → 15/20 | 20/20 → 15/20 |
| False-positive proposals | 10/15 → 0/15 | 10/15 → 0/15 |
| False positives accepted at 0.80 | 5/15 → 0/15 | 5/15 → 0/15 |
| Median confidence on correct proposals | 0.9825 → 0.8345 | 0.9825 → 0.8345 |

Q1 reproduced the memory result, every decision count, and a similar latency regression. Absolute medians were higher on both versions. Direct warm median rose 64% within Q1, compared with 62% in the producer run. Q1 direct working set was 2.29× the Needle 2 value. These are point measurements on one machine. [Producer comparison](../needle3-qualification.md#windows-x64-comparison) · [Benchmark harness](../../benchmarks/CactusNeedleSharp.Benchmarks/NativeComparison.cs)

Retain 0.80. Five correct proposals held back at 0.80 were repetitions of one living-room request, which scored 0.7827. Needle 3's prohibition case scored 0.8634 and was stopped by the negation guard. Use held-out positive, unsupported, negated, and ambiguous cases with representative error costs before reconsidering the threshold. The contradictory error-code probe scored 1.0 and requires a parser fix.

## Packaging and hosted release gates

I independently built both packages twice in distinct Windows checkouts using SDK 10.0.401 and `CI=true`. Both builds passed the package audit. Each primary nuspec identifies 0.3.0, Apache-2.0, the canonical GitHub URL, and the exact candidate commit. The `net8.0` core declares dependency injection and logging abstractions 8.0.2. The worker is a `DotnetTool` that bundles the same core assembly and matching dependencies. NuGet's advisory check reported zero vulnerabilities in the core dependency graph.

Both primary packages contain the README, license and notices, managed binaries, and expected metadata. Native engines, wheels, and model payloads are excluded. Across the two builds, both primary and both symbol archives had identical entry names, ordering, and uncompressed bytes. Whole archive hashes differed and every ZIP entry timestamp differed. Payload reproducibility is established for the tested environment. Whole archives are not byte-identical.

| Primary Q1 build A package | SHA-256 |
|---|---|
| `CactusNeedleSharp.0.3.0.nupkg` | `82622a4ab782d475e3a96215edc514e91f1991b32c27ab1f81925a10715baf9b` |
| `CactusNeedleSharp.Worker.0.3.0.nupkg` | `58e230325fdaa075032db00b96facebf37e50bbdb4b068e87205983a29928ba3` |

The local-only feed installed the worker at version 0.3.0. NuGet's public feed listed versions through 0.2.0 for both IDs. The rebuilt packages contain the exact source SHA, which must be reachable on GitHub for SourceLink consumers.

The [CI workflow](../../.github/workflows/ci.yml) builds, runs managed tests and metadata-only NativeAOT smoke, and audits packages. The [native workflow](../../.github/workflows/needle-integration.yml) defines all eight targets. Its Alpine configuration uses the .NET 10 SDK image and a roll-forward policy, which differs from Q1 Alpine execution on .NET 8. The [publish workflow](../../.github/workflows/publish.yml) can publish on a `v*` tag or manual dispatch after managed tests and audit. It has no prerequisite for a completed native workflow on the same SHA. Public trusted-publishing configuration remains unverified.

## Remediation and release decision

1. Enforce a consistent success envelope and add a regression test for the code-only error case. Verify that both outcome APIs fail closed.
2. Make the disposal test deterministic after startup. Rerun under cold and concurrent Release load without weakening defined timeout behavior.
3. With owner authorization, advance the canonical branch to the remediated candidate and record completed CI and native workflow run IDs for that exact SHA. Obtain live evidence for the five remaining targets or state the verified support scope in public release materials.
4. Preserve the runtime and model pins and the 0.80 confidence policy. Verify trusted publishing and release identity before authorizing a tag or manual publication.

The review commit contains evidence only. This disposition applies to the exact implementation baseline above.
