# CactusNeedleSharp 0.3.0 hosted CI qualification

**CACTUS-NEEDLE-SHARP-N3-RELEASE-CI1 · READY FOR OWNER RELEASE AUTHORIZATION**

## Canonical promotion

| Ref or evidence | Verified identity |
|---|---|
| Repository | `jenolaszlo-sketch/cactus-needle-sharp` |
| Remote main before promotion | `1920988115203a3b7d048dac9d35e00d8f265f95` |
| Clean local main before promotion and promoted remote main | `af4ab60c4ce8430495a07e62ecd46b3e5df9c8e3` |
| Promoted tree | `9681aa5b3844dfe16dc97ff37e616f0c3d334797` |
| Independently qualified implementation | `c703b922ffdb447c8d2be33f6f3206a2bb92cdc4` |
| Implementation tree | `838fd3f132269f70951d4f180a36141c925952c6` |

A fresh fetch and ancestry check established zero remote-only and 11 local-only commits. I inspected each unpublished commit and its changed paths. The history contains the scoped Needle 3 upgrade, remediation, qualification, and release documentation. The registered canonical worktree was clean, with no index lock, and the activity inventory showed no overlapping agent edits. The remote tip was rechecked immediately before a normal, owner-authorized fast-forward. The push returned `1920988..af4ab60` for `refs/heads/main`, and a separate remote lookup returned the promoted SHA. [Promoted commit](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/commit/af4ab60c4ce8430495a07e62ecd46b3e5df9c8e3)

The two additions between the implementation commit and promoted HEAD are the R1 and Q2 review reports under `docs/reviews/`. A diff excluding that directory was empty. The README, production sources, tests, package metadata inputs, dependency declarations and workflow files are identical to the qualified implementation. No source edits were made in promotion. The native engine remains Cactus 4.1.4 at revision `f4ab2741203a282c6e7b89983de2b5e5b2e805a8`, with Needle 3 model revision `d6c7d6a2e9655505e0f8e23339356dd1705900eb`. The manifest still hashes to `ab948753f08e54d2b24dd04830dcb984885524ab3bd1e9da3bf207276ab62eb6`. Worker protocol 2 and the 0.80 confidence threshold were preserved. [Independent qualification](cactus-needle-sharp-n3-release-q2.md) · [Upstream pins](../needle3-upstream.md)

I inspected the repository workflows before pushing. A push to main selects CI and, with the promoted paths, Needle native integration. Publication is limited to a matching tag or manual dispatch. The promotion created neither and triggered only the two qualification workflows. [CI workflow](../../.github/workflows/ci.yml) · [Native workflow](../../.github/workflows/needle-integration.yml) · [Publication workflow](../../.github/workflows/publish.yml)

## Hosted execution

The two runs below were initiated by that push and both have head SHA `af4ab60c4ce8430495a07e62ecd46b3e5df9c8e3`. Results come from the GitHub job records and logs. Native integration passed on attempt 1. CI passed on attempt 2 after a retry limited to its cancelled macOS job.

| Workflow | Run ID | Conclusion |
|---|---:|---|
| [CI](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418647240) | 45418647240 | Success on attempt 2 |
| [Needle native integration](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418646791) | 45418646791 | Success |

| CI job | Job ID | Conclusion and evidence |
|---|---:|---|
| [Build and managed tests, Windows](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418647240/job/138531735953) | 138531735953 | Success, 70/70 tests |
| [Build and managed tests, Ubuntu](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418647240/job/138531735726) | 138531735726 | Success, 70/70 tests |
| [Build and managed tests, macOS retry](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418647240/job/138542969777) | 138542969777 | Success, 70/70 tests |
| [NativeAOT smoke, Ubuntu](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418647240/job/138531736046) | 138531736046 | Success, compiled and executed the sample |
| [Package build and audit](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418647240/job/138531735362) | 138531735362 | Success, two primary archives and two symbol archives |

| Native platform | Job ID | Conclusion and count |
|---|---:|---|
| [Windows x64](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418646791/job/138531734180) | 138531734180 | Success, 8/8 |
| [Windows ARM64](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418646791/job/138531733983) | 138531733983 | Success, 8/8 |
| [Linux glibc x64](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418646791/job/138531734194) | 138531734194 | Success, 8/8 |
| [Linux glibc ARM64](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418646791/job/138531733884) | 138531733884 | Success, 8/8 |
| [Linux musl x64](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418646791/job/138531734635) | 138531734635 | Success, 8/8 |
| [Linux musl ARM64](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418646791/job/138531733262) | 138531733262 | Success, 8/8 |
| [macOS x64](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418646791/job/138531733950) | 138531733950 | Success, 8/8 |
| [macOS ARM64](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418646791/job/138531734321) | 138531734321 | Success, 8/8 |

The native logs establish 64 successful test executions. The suite exercises verified artifact selection, tampered cache and incompatible model rejection, offline cache reuse, inference, typed extraction, and isolation and recovery with real matching workers. The Alpine jobs use the configured .NET 10 host with roll-forward for the net8.0 tests. The hosted NativeAOT job exercises build and serialization smoke without a seeded native cache. Q2 supplies the complementary offline native inference and extraction result with NativeAOT. The managed jobs logged 70/70 each, for 210 successful test executions across the three runners. No configured job remains skipped or inconclusive.

CI attempt 1 concluded failure because GitHub cancelled the [original macOS job](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418647240/job/138531735766) after about 15 minutes in the queue. The job had no assigned runner or executed steps and its log endpoint returned 404. GitHub annotated it as a hosted-runner acquisition failure and noted macOS ARM64 capacity constraints. I retried only that job on the same commit. Job `138542969777` acquired a runner and passed all 70 tests, including the R1 disposal regression. This recovered the infrastructure failure with no code or test changes.

## Hosted NuGet evidence

The [`nuget-packages` workflow artifact](https://github.com/jenolaszlo-sketch/cactus-needle-sharp/actions/runs/45418647240/artifacts/14932937649) has ID `14932937649`. GitHub reports the artifact digest `sha256:c2503afcc48d4f70076c0fc31a206c6f9e221530e1c2becbbbd373e9a66d82c5`. The hosted pack logs verified the README, Apache-2.0 licensing, notices, and exclusion of native and model files. I downloaded the artifact and repeated the audit. The four nuspecs name the canonical repository and promoted SHA and report version 0.3.0. The hosted core DLL bytes match the core embedded in the worker package.

| Archive | SHA-256 |
|---|---|
| `CactusNeedleSharp.0.3.0.nupkg` | `e8385ba93af10cda4c61df1588033f12cd11ee8731382ea77dd7d12d441af075` |
| `CactusNeedleSharp.Worker.0.3.0.nupkg` | `2ee256a907911df7250fde893ceb073086d2a1c880093423023935a39c21a9ec` |
| `CactusNeedleSharp.0.3.0.snupkg` | `11b29a420d45c1c378f97c8dca1b4a3b6fbd3da285263e5211b5c4a3387baa76` |
| `CactusNeedleSharp.Worker.0.3.0.snupkg` | `f9ea26d8cb341dbeb2c9bd884c2279b87968d66c5c9e3453b855f49f803c56ab` |

The native workflow verifies matching core and worker behavior. Cross-version rejection was verified separately with the downloaded hosted assemblies on a local host. The 0.3.0 core rejected the real 0.2.0 worker's protocol 1 response, and the 0.2.0 core rejected the hosted 0.3.0 worker's protocol 2 response. Both left zero workers registered. The hosted worker assembly reported 0.3.0.0 and protocol 2 and accepted a clean shutdown.

## Release scope and recommendation

The README still describes Needle 3 and the independently reproduced Q1 benchmark. The benchmark values remain 181 → 297 ms median, 59 → 135 MiB direct working set, 20 → 15 accepted correct proposals, and 5 → 0 accepted false positives at 0.80. No benchmark figures were changed. [README comparison](../../README.md#independent-benchmark)

The frozen Q2 report preserves the local live-execution gaps. CI1 adds hosted native execution for all ARM64 and macOS targets in the matrix. Those results do not extend independent offline qualification beyond the Q2 x64 environments or establish NativeAOT on ARM64 and macOS. Independently fine-tuned weights remain unqualified.

Recommend submitting 0.3.0 for explicit owner publication authorization. Both hosted workflows succeeded for the promoted SHA, and the package audit and source identity checks passed. Release approval, NuGet publication, and tag or GitHub release creation remain separate actions. This CI1 report is committed locally so canonical `main` continues to identify the exact tested and packaged commit.
