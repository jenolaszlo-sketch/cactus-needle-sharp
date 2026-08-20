# Licensing

Original CactusNeedleSharp wrapper source is licensed under Apache License 2.0. That license applies only to the independently authored .NET wrapper.

Needle, Needle 2, model weights, native runtime components, Cactus technology, and other upstream artifacts retain their respective upstream licenses and copyright notices. The wrapper license does not relicense those works.

The NuGet package contains wrapper code only. At runtime, the default artifact provider obtains the native library from the official `Cactus-Compute/needle2` distribution and caches it separately. It does not alter an upstream binary or its governing terms. Where an upstream distribution includes adjacent license or notice files, those files must remain intact; the wrapper's root `LICENSE` must never replace them.

Before every release, verify the licenses and notices attached to the current Needle repository, Needle 2 model, native runtime, and any other downloaded artifact. Update `THIRD_PARTY_NOTICES.md` if upstream terms or notices change, and inspect the packed NuGet archive to ensure no upstream artifact was included.

The artifact provider preserves license and notice files found inside the official runtime wheel under an `upstream-notices` subdirectory of the runtime cache. See `release-checklist.md` for the required release review.
