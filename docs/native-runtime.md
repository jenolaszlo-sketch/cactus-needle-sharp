# Native runtime

Resolution order is `NeedleOptions.NativeLibraryPath`, upstream `NEEDLE_LIB_PATH`, and the wrapper cache. Set `NeedleOptions.Offline = true` to guarantee that a missing artifact fails without network access.

The default cache is `%LOCALAPPDATA%/CactusNeedleSharp/2.0.3` on Windows and the corresponding .NET local-application-data folder elsewhere. `CacheDirectory` overrides it.

The official ABI has no interruption function. Cancellation is observed before and after inference, but native compute may run to completion internally.

Official 2.0.3 wheel sizes and SHA-256 hashes are pinned per supported platform. Installation verifies the wheel before extraction and writes `artifact-manifest.json` with the wheel identity and extracted native-library SHA-256. Cached and offline runtimes are rehashed before use. Explicit native-library paths can be pinned with `ExpectedNativeLibrarySha256`; set `VerifyArtifactIntegrity = false` only for a consciously managed development artifact.
