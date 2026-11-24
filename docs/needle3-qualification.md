# Needle 3 qualification

**Disposition for the proposed 0.3.0 release: READY WITH QUALIFICATIONS.**

The proposed managed version is **0.3.0** for both existing package IDs. The pre-1.0 minor release retains existing constructors, interfaces, and source-generated extraction overloads. The native artifact pair and the worker protocol change together. Use a 0.3 core with a 0.3 worker. [Migration and cache layout](native-runtime.md)

## Artifact evidence

The upstream source baseline is `cactus-compute` 4.2.7 at commit `3b0898b99d2479a674a453827f01d3c20099896f`. Cactus 4.1.4 comes from engine revision `f4ab2741203a282c6e7b89983de2b5e5b2e805a8`. Needle 3 comes from model revision `d6c7d6a2e9655505e0f8e23339356dd1705900eb`. The [upstream assessment](needle3-upstream.md) records the C ABI and the pinned model and manifest hashes. The [unmodified manifest](cactus-native-4.1.4-manifest.json) records each downloaded wheel's size and SHA-256.

All eight downloaded wheels matched that manifest. Every extracted library matched the declared architecture in its PE, ELF, or Mach-O header. The managed cache verifies the following extracted-library identities directly.

| Platform | Library bytes | Library SHA-256 |
|---|---:|---|
| Windows x64 | 1,914,880 | `a28490bc7826c7b0ee9c51bc3092a26ec935a85d08ea82a7ed3593fadde5b381` |
| Windows ARM64 | 1,646,080 | `a21029b12ea6389c18c9d799e51599910bb7546814fef70e1d7a1e841c4610aa` |
| Linux glibc x64 | 1,981,624 | `6c677ec261ab8f50802e8d9c261cd5fbfa0a7be35cef3d90b62b4f80f4125715` |
| Linux glibc ARM64 | 1,830,632 | `4ce9d02bb35cf40f5c60965598d4b9556c956379cebe65187b936430a4b56c17` |
| Linux musl x64 | 1,986,728 | `fda663dd0bba027e6d5f80d7326f31f4df4015ca3b76b0dc3a63361d046d6aa9` |
| Linux musl ARM64 | 1,834,232 | `08113140f00d662c88d8e39be7a72adc07b23d139048b90d1939a230575632db` |
| macOS x64 | 1,689,112 | `09d6491861aa7d1025defce2fbeb551a66764c3ba24e7e3e3808c4fd9003c10e` |
| macOS ARM64 | 1,451,664 | `f28e1f4f7792a4649b4e91d94f9a17f0c037f41a19b4b848f2f47ba960231ddd` |

## Local qualification

| Target | Execution evidence |
|---|---|
| Windows x64 | 8 official-model and real-worker tests passed, including download and offline-cache paths |
| Windows ARM64 | Artifact verified. Hosted test pending |
| Linux glibc x64 | 8 official-model and real-worker tests passed on Ubuntu 26.04.1 WSL2, glibc 2.43, .NET 8.0.33, including a rerun in a loopback-only network namespace |
| Linux glibc ARM64 | Artifact verified. Hosted test pending |
| Linux musl x64 | All 8 acceptance methods passed with the official musl library on Alpine 3.23.8 and .NET 8.0.33 in a loopback-only WSL2 namespace |
| Linux musl ARM64 | Artifact verified. Hosted test pending |
| macOS x64 | Artifact verified. Hosted test pending |
| macOS ARM64 | Artifact verified. Hosted test pending |

The Alpine run invoked the same eight test methods directly after the local chroot's VSTest host failed to attach. The managed code, model, and real worker were exercised. The configured Alpine CI jobs remain pending.

The unchanged Needle 2 baseline passed 45 managed tests. The upgraded Windows Release suite passed 63 managed tests. The real tests cover ordinary tool selection, multi-call proposals, constraints and optional arguments, UTF-8, malformed managed input, multi-turn reset, typed extraction, refusal, low-confidence policy, suppression, and native negation. The worker tests include simultaneous conversations, queueing, isolation after reuse, startup failure, process crash recovery, repeated sessions, and a compatible model variant with distinct bytes. Managed protocol fault tests cover worker cancellation and timeout handling.

The compatible-model test changes a padding byte in the pinned model and preserves all tensor payloads. It exercises runtime selection, confidence-head reporting, unknown calibration, and worker process transitions. An independently fine-tuned model was outside the executed campaign.

The Linux glibc qualification found that a missing city could yield an empty string when the declared schema allowed one. With a nonempty city constraint, the Windows and glibc x64 engines produced suppressions for the test prompt. A prohibited weather request on both platforms could still produce a native proposal with negation metadata. The managed outcome policy withholds that proposal. These are observations from the pinned model and tested inputs.

The Linux x64 NativeAOT sample passed with the network disabled. It exercised source-generated tool schema and response decoding and typed extraction against the pinned local artifacts. The sample uses the patched 8.0.33 NativeAOT compiler/runtime. Both local 0.3.0 NuGet packages passed the package contents audit. The managed Release build completed without warnings. GitHub Actions jobs are configured for the eight platforms. The qualification above records local executions. No hosted run exists for the local implementation commits.

## Windows x64 comparison

Measured sequentially on one Intel Core Ultra 5 125H host with 18 logical processors, Windows build 26200, and .NET 8.0.31. The Needle 2 baseline was commit `1920988115203a3b7d048dac9d35e00d8f265f95` with the verified 2.0.3 Windows x64 library and its embedded weights. Needle 3 used Cactus 4.1.4 and the pinned Needle 3 model. Both runs used already-present artifacts, automatic native thread configuration, the same harness, and an independent process. Needle 3 was measured at `de8117119188520d9e7a648ec3fd5a3e48a79743`.

One warm-up request preceded each timing series. The harness measured each of seven prompts five times per transport, resetting the session before each request. Cold initialization includes artifact resolution and first-session initialization, before the first inference. Worker handshake and first initialization are separate observations. Percentiles use nearest rank.

| Metric | Needle 2.0.3 | Needle 3 / Cactus 4.1.4 |
|---|---:|---:|
| Cold client and first session | 271.7 ms | 360.9 ms |
| Direct warm median | 147.1 ms | 238.4 ms |
| Direct warm p95 | 250.7 ms | 346.4 ms |
| Direct median prefill | 563.2 tokens/s | 413.3 tokens/s |
| Direct median decode | 261.7 tokens/s | 192.8 tokens/s |
| Direct working set after inference | 59.25 MiB | 134.92 MiB |
| Direct working-set increase | 33.37 MiB | 108.96 MiB |
| Direct peak working set | 59.25 MiB | 134.92 MiB |
| Worker startup and handshake | 330.8 ms | 158.1 ms |
| First session on prestarted worker | 505.9 ms | 307.0 ms |
| New session on reused base worker | 1.3 ms | 47.8 ms |
| Worker warm median | 139.6 ms | 248.3 ms |
| Worker warm p95 | 264.8 ms | 329.1 ms |
| Worker median prefill / decode | 511.1 / 258.7 tokens/s | 370.7 / 193.4 tokens/s |
| Worker working set / peak | 62.45 / 63.13 MiB | 137.57 / 137.57 MiB |
| Correct tool and arguments | 20/20 | 20/20 |
| Correct proposals meeting 0.80 policy | 20/20 | 15/20 |
| False refusals by proposal count | 0/20 | 0/20 |
| No-call or withheld on negative prompts | 5/15 | 15/15 |
| False-positive proposals / accepted at 0.80 | 10/15 / 5/15 | 0/15 / 0/15 |
| Median confidence on correct proposals | 0.9825 | 0.8345 |

Needle 3's direct median was 62% slower and its direct working set was 2.28× as large. The one-shot worker startup measurements vary with OS scheduling. Reusing an initialized worker avoided process startup on both versions. Needle 3 still verified cached model bytes during reused session initialization on this run.

Both transports reproduced the quality counts. Needle 2 proposed a call for the explicit prohibition with confidence 0.9571. Needle 3 blocked the prohibited request even though its reported confidence was 0.8634. Needle 3's correct living-room proposal scored 0.7827 and was withheld by the unchanged 0.80 threshold, accounting for five fewer accepted correct proposals. Confidence appeared in all responses on both versions. This seven-prompt set illustrates observed confidence behavior and cannot establish population-level calibration.

Reproduce using the Release `CactusNeedleSharp.Benchmarks` project with `--native-comparison <native-library> <worker-apphost>`. Set `NEEDLE_TEST_CACHE_DIRECTORY` to a preverified cache for Needle 3. For the baseline, copy the harness files `Program.cs` and `NativeComparison.cs` into the baseline benchmark project and point it at the baseline worker and verifiable 2.0.3 library. All timings and decisions in the table came from the native engines.

## Scope and remaining qualification

The wrapper preserves the proposal-only contract. Schema restrictions, invalid proposals, suppression, confidence, and native validation signals are enforced in the recommended outcome policy. Native detection and confidence can still make semantic mistakes. Upstream Python adds date facts, deterministic repair, and stricter extraction heuristics, as covered in [the runtime investigation](runtime-investigation.md).

The public API changes are additive. `INeedleSession.RuntimeInfo` reports the selected runtime and weights, and `NeedleClient.RuntimeInfo` reports its default. Model transitions require a fresh process. Workers negotiated with protocol 1 must be upgraded with the core. Unsupported JSON Schema assertions require a schema supported by the managed verifier before a proposed call can receive a successful outcome.

The primary implementation is in `Native/NeedleNative.cs`, `Artifacts/HuggingFaceNeedleArtifactProvider.cs`, `Artifacts/NeedleModelFormat.cs`, `NeedleClient.cs`, `Serialization/NeedleProtocol.cs`, and `Serialization/NeedleArgumentSchema.cs`. Worker process selection and metadata change in `Workers/` and the worker host. The companion changes update native integration tests, benchmark and sample entry points, CI, packaging review, and migration and licensing documentation.
