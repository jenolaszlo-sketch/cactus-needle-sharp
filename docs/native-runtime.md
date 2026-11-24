# Native runtime

CactusNeedleSharp 0.3 uses Cactus 4.1.4 and Needle 3. The default provider downloads the shared library and the model independently using immutable Hugging Face revisions. A Cactus wheel is used as a ZIP archive containing a loadable native library. The .NET application and worker run locally without Python or a resident service. [Upstream assessment and verified pins](needle3-upstream.md)

The default cache root is the .NET local application data directory under `CactusNeedleSharp`. Set `NeedleOptions.CacheDirectory` to override it. The cache uses the following directory layout.

```text
engine/4.1.4/<engine-revision>/<platform-tag>/libcactus.<dll|so|dylib>
models/needle3/<model-revision>/needle3.cact
```

Engine wheels, extracted libraries, and official weights are checked against pinned sizes and SHA-256 hashes. Verified library and model files are installed by same-directory atomic rename under separate cross-process locks. Cached and offline files are checked directly against pinned digests. Set `Offline = true` to prevent downloads. `VerifyArtifactIntegrity` is retained for source compatibility and cannot disable official pins or caller-supplied checksums.

`NativeLibraryPath` overrides the default engine. The provider also recognizes `NEEDLE_LIB_PATH` for existing callers and `CACTUS_LIB_PATH` for the upstream Cactus convention. `ModelPath` selects client-default weights. `NeedleSessionOptions.WeightsPath` selects weights for a single session and otherwise inherits the client default. When using distinct paths and a sealed deployment, set `ExpectedNativeLibrarySha256` and `ExpectedModelSha256` for your explicit default artifacts. Explicit paths whose contents match the pins report official upstream identity. Other explicit libraries report the caller's version label or `unknown`, and explicit weights report their digest and the presence of a confidence head. The wrapper permits calibrated confidence only for a verified official engine/model pair.

Needle 2 libraries lack the Cactus 4 exports and Needle 2 weights have a different archive tag. Replace both during deployment and populate the new cache before switching a disconnected host. The wrapper rejects legacy weights before native load. It verifies the Needle 3 archive header, tensor directory, and payload bounds before handing bytes to Cactus. Native loading remains the final compatibility check for caller-selected weights.

The process retains the first successfully loaded model identity. A different model or a failed native model load requires a fresh process. Cancellation is checked before and after native inference. If cancellation arrives during native execution, the caller receives cancellation after Cactus returns and that turn may have advanced conversation state. A worker can be terminated to abandon an in-progress call. [Worker lifecycle](worker-pool.md)

Pinned wheels supply shared libraries for Windows x64 and ARM64, Linux glibc and musl x64 and ARM64, and macOS x64 and ARM64. Linux musl selection uses the runtime identifier or the actual musl loader. Qualification depends on executing on each host. [Qualification status](needle3-qualification.md)
