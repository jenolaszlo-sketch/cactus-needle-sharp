# CactusNeedleSharp 0.3.0 R1 remediation

**CACTUS-NEEDLE-SHARP-N3-RELEASE-R1 · READY FOR INDEPENDENT REQUALIFICATION**

| Item | Identity |
|---|---|
| Remediation candidate and package source commit | `c703b922ffdb447c8d2be33f6f3206a2bb92cdc4` |
| Candidate tree | `838fd3f132269f70951d4f180a36141c925952c6` |
| Independent Q1 parent | `bf9f13f84fda244fd85d4103371ff859e77bc1cd` |
| Engine | Cactus 4.1.4 at `f4ab2741203a282c6e7b89983de2b5e5b2e805a8` |
| Model | Needle 3 at `d6c7d6a2e9655505e0f8e23339356dd1705900eb` |
| GitHub main at verification | `1920988115203a3b7d048dac9d35e00d8f265f95` |

The candidate preserves the public API, worker protocol 2, and the 0.80 confidence threshold. The runtime, model, worker production code, package version, manifest, and CI definitions are unchanged from Q1. The manifest still hashes to `ab948753f08e54d2b24dd04830dcb984885524ab3bd1e9da3bf207276ab62eb6`. The model used for the Alpine run still hashes to `c9d915eca282ed42d1a09b143b592adb4cc6744ffe2d294adf5cfc5548170c38`.

## R1-01 · Shared response boundary

The parser previously used the native success flag to admit calls. Its consistency guard considered error text but did not check `error_code` on a success envelope. The shared parser now computes effective success before admitting proposals. A nonblank native error message or error code takes precedence over a declared success. The resulting compilation has `Success=false`, empty `Calls`, retained diagnostic fields, and any otherwise well-formed proposals only in `RejectedCalls`. The existing outcome and confidence APIs therefore reject it even with confidence 1. Typed extraction skips decoding and uses the code when error text is absent. Worker responses carry that same failed compilation through the existing protocol. Malformed envelopes still raise a protocol exception.

Before the fix, three of four focused tests failed, reproducing the code-only error accepted as success in both the parser and the worker-pool path. The error-text case threw instead of preserving failed-compilation diagnostics. The already-failed envelope with populated calls passed its safety assertions. All seven added regression cases passed on the candidate, including normal empty and suppressed responses and a malformed failure without diagnostics. Existing successful proposals, schema checks, suppression, negation, and low-confidence cases passed as well.

The separately built Q1 probe repeated ten boundary cases on the candidate. Both the text-only and code-only contradictory responses changed to `Failed`. In a real offline Windows `CompileAsync` probe, the out-of-range thermostat request returned `Success=false`, zero calls, `Failed`, and native code `truncated` with its diagnostic text. A separate exploratory low-budget request yielded a failed outcome with zero calls and no native code. The synthetic Q1 reproduction remains the evidence for the specific contradictory envelope.

## R1-02 · Worker startup and disposal

Q1's 200 ms `RequestTimeout` capped every request, including startup ping, inside the separate startup token. The observed timeout was in the fake worker's ping before session creation, so the disposal assertion was never reached. This fixture performs no native load or artifact acquisition. Production startup and capacity code were reviewed and are unchanged. The Q1 failure demonstrates sensitivity to cold subprocess and pipe scheduling under concurrent load with that test configuration. It provides no evidence of an artifact-cache lock or a production pool capacity defect.

The test now creates the session before applying its cancellation stimulus. The first completion takes the session's single-flight lease and returns a pending task. The test checks that two disposal calls share the same pending task, cancels the completion explicitly, and then verifies cancellation, both disposals, removal of the canceled worker, and successful replacement within the same one-worker pool. This preserves the disposal and cancellation assertions without using a 50 ms sleep or a startup-sensitive timeout. Production request, startup, queue and shutdown deadlines have their prior behavior.

On Windows x64 with .NET SDK 10.0.401 and `CI=true`, four joint Release rounds ran the managed and native test assemblies concurrently. They finished 70/70 managed and 8/8 native every round, for 280 managed and 32 native passes. Round 1 began with an empty Windows artifact cache. Three additional concurrent lanes each ran the affected disposal test four times, overlapping the joint tests and Linux qualification work. All 12 targeted runs passed. The joint rounds add another four executions of the affected test.

## R1-03 · Native and packaging requalification

| Check | Candidate result |
|---|---|
| Windows x64 native acceptance | 8/8 in each of four rounds |
| Linux glibc x64 | 8/8 in a loopback-only WSL2 namespace using the verified cache |
| Linux musl x64 | 8/8 by direct invocation of the existing acceptance methods in loopback-only Alpine 3.23.8, using .NET 8.0.33 and a real worker |
| Linux glibc x64 NativeAOT | Published and passed offline real inference and source-generated extraction |
| Windows ARM64, Linux glibc/musl ARM64, macOS x64/ARM64 | Q1 artifact evidence applies to the unchanged pins. Live execution remains open |
| Independently fine-tuned weights | Unqualified |

Both 0.3.0 NuGet packages and both symbol archives were rebuilt from the candidate above with `CI=true`. Both primary packages passed the repository audit for documentation, Apache-2.0 licensing, notices and exclusion of native/model files. All four nuspecs identify the exact candidate SHA and version. The worker contains the same core DLL as the core package.

| Primary package | SHA-256 |
|---|---|
| `CactusNeedleSharp.0.3.0.nupkg` | `9ba5338bef1e187842c7d4a51f221208cffceddc1adb23778b6308b94d767d64` |
| `CactusNeedleSharp.Worker.0.3.0.nupkg` | `bd23dd9be58b67fd0ce32c4cd78b78e7972317c9e3d62ee8b0f9cb2b420dcd1f` |

## R1-04 · Hosted handoff

A read-only `git ls-remote` check confirmed that GitHub main is an ancestor of the candidate, with zero remote-only and nine candidate-only commits. The GitHub Actions API returned zero runs for the exact candidate. Hosted execution is pending owner authorization to push. Use a normal fast-forward for the candidate and capture the CI and Needle native integration run IDs and outcomes for the SHA and tree above. Compare their package metadata to that exact commit. This evidence commit follows the candidate as a documentation-only descendant and is not the package source. Publication approval remains separate.

## R1-05 · Documentation and changed files

The README preserves the upgrade work and now shows Q1's independently reproduced 181 → 297 ms, 59 → 135 MiB, 20 → 15 accepted correct proposals and 5 → 0 accepted false positives. It links the separately labelled producer comparison. Successful inference behavior and configuration were unchanged, so R1 used the existing benchmark evidence. [Independent benchmark](cactus-needle-sharp-n3-release-q1.md#independent-benchmark)

The candidate changes six files:

- [NeedleProtocol.cs](../../src/CactusNeedleSharp/Serialization/NeedleProtocol.cs) and [NeedleExtractionResults.cs](../../src/CactusNeedleSharp/NeedleExtractionResults.cs) for failure classification and diagnostics.
- [Test worker](../../tests/CactusNeedleSharp.TestWorker/Program.cs), [parser regressions](../../tests/CactusNeedleSharp.Tests/Needle3ProtocolTests.cs), and [worker-pool tests](../../tests/CactusNeedleSharp.Tests/WorkerPoolTests.cs) for coverage and disposal synchronization.
- [README.md](../../README.md) for benchmark reconciliation.
