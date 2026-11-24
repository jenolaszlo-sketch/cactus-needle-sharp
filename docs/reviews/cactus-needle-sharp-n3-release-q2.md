# CactusNeedleSharp 0.3.0 independent Q2 qualification

**CACTUS-NEEDLE-SHARP-N3-RELEASE-Q2 · 2026-11-24 · LOCALLY QUALIFIED, HOSTED GATE PENDING**

Q2 independently confirmed the R1 fail-closed and worker disposal remediations. Local release checks passed. Hosted CI was **NOT EXECUTED** for this candidate, and public release remains gated on hosted results and owner authorization.

## Candidate and evidence identity

| Item | Verified identity |
|---|---|
| Repository | `jenolaszlo-sketch/cactus-needle-sharp` |
| Candidate and package source commit | `c703b922ffdb447c8d2be33f6f3206a2bb92cdc4` |
| Candidate tree | `838fd3f132269f70951d4f180a36141c925952c6` |
| Q1 implementation | `596b961727fc67a543799679453b777e49ddf3fc` |
| Q1 report, direct parent of candidate | `bf9f13f84fda244fd85d4103371ff859e77bc1cd` |
| R1 evidence, direct descendant of candidate | `672f363481642201f1b46cbf173a2d2ef2d9d5a4` |
| Target packages | `CactusNeedleSharp` and `CactusNeedleSharp.Worker` 0.3.0 |
| Engine | Cactus 4.1.4 at `f4ab2741203a282c6e7b89983de2b5e5b2e805a8` |
| Model | Needle 3 at `d6c7d6a2e9655505e0f8e23339356dd1705900eb` |
| Engine manifest SHA-256 | `ab948753f08e54d2b24dd04830dcb984885524ab3bd1e9da3bf207276ab62eb6` |
| Needle 3 model SHA-256 | `c9d915eca282ed42d1a09b143b592adb4cc6744ffe2d294adf5cfc5548170c38` |

The canonical local checkout was clean on `main` at the R1 evidence commit before Q2. Builds and tests used clean independent checkouts of the exact candidate. The candidate diff contains six files covering the parser, extraction diagnostics, three test files and README. The ABI and artifact-provider files, public declarations, worker production code, package version, CI, and default 0.80 policy match Q1. The only difference from the candidate to its R1 evidence descendant is the R1 report. The Q2 commit adds this report and preserves the candidate, README, diagrams and prior evidence. [Q1 findings](cactus-needle-sharp-n3-release-q1.md) · [R1 evidence](cactus-needle-sharp-n3-release-r1.md)

## Fail-closed verification

Q2 used a separate probe and an ABI-compatible test library outside the repository. The probe invokes the candidate parser and also feeds synthetic native envelopes through the real managed client and worker host. This permits an exact challenge through public APIs regardless of what the production model happens to emit. The actual pinned-engine acceptance runs are reported separately.

| Independent observation | Result |
|---|---|
| Valid tool proposal at native confidence 1.0, `success:true`, `error_code:"native_failure"` | `Success=false`, `GetOutcome()=Failed`, `IsConfident(0.80)=false`, and zero executable calls. The error code and rejected proposal remain diagnostic data |
| Contradictory error text or explicit failure with populated calls | Direct compilation and worker transport expose no executable calls, retain diagnostics and classify the result as failed |
| Malformed JSON | Direct compilation raises a protocol exception. The real worker reports failure to its caller |
| Positive controls | Direct and session proposals survive, reset retains normal behavior, and both direct and worker typed extraction return a valid value for a clean response |
| Confidence boundary | Raw valid proposals at 0.8000 and 0.7999 give `Success` and `LowConfidence` respectively. Failed results remain ineligible even at threshold zero |

The direct session, one-shot client and real worker host all use the same native parser. In that parser, meaningful error text or code overrides the declared success value before calls can enter `Calls`. Both typed extraction paths returned no value for the code-only failure and retained its diagnostic. The explicit fixture library has unknown calibration, so the raw parser probe separately verified that confidence 1.0 was present and still could not override the failure. [Shared parser](../../src/CactusNeedleSharp/Serialization/NeedleProtocol.cs) · [Extraction projection](../../src/CactusNeedleSharp/NeedleExtractionResults.cs)

## Worker lifecycle and concurrency

The revised disposal unit test starts the session before requesting completion, so the former 200 ms cold-start dependency is absent. The first completion holds the single-flight lease before returning its pending task. The test checks that both disposal calls share that pending task, cancels the completion, then checks activity termination and usable replacement capacity. This establishes lifecycle ordering at the managed session boundary.

A separate Q2 rendezvous confirmed arrival inside `cactus_complete` in a real worker before either disposal call. The probe verified the two pending disposal calls shared a task and the worker stayed leased until cancellation. It observed process exit, zero active, idle and waiting counts, and a successful call on a replacement worker. A one-shot native init error and a canceled startup before ping each left zero registered workers and permitted a retry in the same pool. The startup probe also checked that the blocked launcher PID exited. A trailing carriage return in the scratch launcher's final command broke its first restart attempt. Normalizing the fixture's line endings and rerunning with fresh rendezvous files passed all checks. The candidate and production timeout settings were preserved.

On Windows x64, SDK 10.0.401 and runtime 8.0.31, all three joint Release rounds passed 70/70 managed and 8/8 native. Managed and native assemblies ran concurrently within each round. Round one began with an empty Windows cache. The affected disposal test then passed 12 additional executions across three concurrent lanes, plus its three executions within the full suite. Total Q2 Windows coverage was 210 managed test passes and 24 native test passes, with zero suite failures. No production startup, request or shutdown timeouts were raised. Both actual 0.2.0 and 0.3.0 cross-version core/worker pairings were rejected during handshake and registered zero workers.

## Native regression and platform matrix

| Platform | Q2 execution | Evidence scope |
|---|---|---|
| Windows x64 | 8/8 in each of three rounds | Official model and real worker. Fresh acquisition and verified cached reuse |
| Linux glibc x64 | 8/8 | Official runtime and weights in a loopback-only Ubuntu WSL2 namespace |
| Linux musl x64 | 8/8 | Same acceptance methods invoked directly in loopback-only Alpine 3.23.8 with .NET 8.0.33 and a real worker |
| Windows ARM64 | Artifact verification from Q1 | Live execution pending |
| Linux glibc ARM64 | Artifact verification from Q1 | Live execution pending |
| Linux musl ARM64 | Artifact verification from Q1 | Live execution pending |
| macOS x64 | Artifact verification from Q1 | Live execution pending |
| macOS ARM64 | Artifact verification from Q1 | Live execution pending |

Acceptance covered model load, reset, verified cache rejection of tampered artifacts, tool selection, schema constraints, typed extraction, suppression, negation, and worker conversation isolation. The Q2 Windows installation had the pinned 1,914,880-byte engine SHA-256 `a28490bc7826c7b0ee9c51bc3092a26ec935a85d08ea82a7ed3593fadde5b381` and the pinned 35,335,380-byte model. The Linux glibc NativeAOT executable was rebuilt and passed real offline inference and source-generated typed extraction. The Alpine run uses the exact candidate test methods with a direct runner, so it does not qualify the hosted Alpine VSTest configuration. Independently fine-tuned weights remain unqualified. [Integration procedures](../integration-testing.md)

## Packages and README

Both 0.3.0 packages and both symbol archives were rebuilt in two separate clean Windows checkouts using `CI=true`. Both primary packages passed the existing content and licensing audit on each build. Their nuspecs give the canonical repository URL and exact candidate commit. Core and worker versions align and their embedded core DLL bytes match. The packages contain the README, license and notices, with native runtimes and model payloads excluded.

The four archive pairs had identical paths, ordering and uncompressed bytes. ZIP entry timestamps differed in all 10 core package entries, 18 worker package entries, five core symbol entries and six worker symbol entries. Full archive hashes differed accordingly. Payload equivalence passed.

| Q2 build A primary package | SHA-256 |
|---|---|
| `CactusNeedleSharp.0.3.0.nupkg` | `30c9aaa9105697f2174f5ea5f021fe3afb3b827bce2aaba1fcf48452a004ec67` |
| `CactusNeedleSharp.Worker.0.3.0.nupkg` | `8c5cace76bbbb2498811ce02d81a655f07e5403218ef1e3a5ad4899f5f6982d9` |

The README migration guidance matches the verified default engine/model, separate caches, `Offline=true`, and explicit engine and model paths. Its benchmark rows match the independent Q1 run, with Needle 2 to Needle 3 changing from 181 to 297 ms warm median, 59 to 135 MiB working set, 20 to 15 accepted correct proposals, and five to zero accepted false positives at 0.80. The comparison remains tied to the Q1 benchmark, which labels the earlier producer run separately. Q2 left benchmark figures and documentation intact. [README](../../README.md#independent-benchmark) · [Q1 comparison](cactus-needle-sharp-n3-release-q1.md#independent-benchmark)

## Hosted CI and remaining release gates

A read-only remote check returned GitHub `main` at `1920988115203a3b7d048dac9d35e00d8f265f95`. It is an ancestor of the exact candidate, with zero remote-only and nine candidate-only commits. GitHub's Actions API returned zero runs for `c703b922ffdb447c8d2be33f6f3206a2bb92cdc4`. **Hosted CI: NOT EXECUTED.** There are no candidate run IDs or conclusions to record. [Remote baseline](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/commit/1920988115203a3b7d048dac9d35e00d8f265f95)

After owner authorization, advance canonical `main` via the normal workflow and run CI and Needle native integration against the exact candidate commit and tree above. Capture the run links, IDs and conclusions for that SHA and compare the hosted package source metadata. Resolve or explicitly qualify the five live-platform gaps and the fine-tuned-weight gap before claiming broader support. Public NuGet publication and GitHub release creation remain separate approvals.
