# Native integration testing

Run the managed suite with `dotnet test CactusNeedleSharp.slnx -c Release`. Native tests are opt-in. Set `NEEDLE_RUN_INTEGRATION_TESTS=1`, `NEEDLE_EXPECTED_ARCHITECTURE` to `X64` or `Arm64`, and `NEEDLE_TEST_CACHE_DIRECTORY` to an isolated cache path. Set `NEEDLE_WORKER_PATH` to the Release worker apphost, which is built with the integration test project. Run the integration project with the native category filter.

```text
dotnet test tests/CactusNeedleSharp.IntegrationTests -c Release --filter Category=NeedleIntegration
```

A fresh cache exercises acquisition and verification. Keep the cache and rerun under a network-disabled process or container to show verified offline inference. Tests cover cached and tampered artifacts, model format rejection, tool selection, multiple calls, argument constraints, native suppression and negation, Unicode, typed extraction, worker isolation, and capacity recovery. Managed fault tests also exercise timeouts and cancellation.

To run native inference from the NativeAOT sample, publish `samples/AotSmoke` for your RID and point `NEEDLE_TEST_CACHE_DIRECTORY` at an already verified cache for that platform. Without the cache environment variable, the executable exercises metadata and serialization paths without loading native artifacts.

The native GitHub Actions workflow defines Windows, Linux glibc, and macOS x64 and ARM64 jobs. Linux musl jobs run on same-architecture hosts using the Alpine .NET 10 SDK image and roll the net8.0 test host forward to the installed runtime. See [recorded qualification](needle3-qualification.md) for jobs and hosts that have actually executed.
