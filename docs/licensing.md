# Licensing

Original CactusNeedleSharp wrapper source is licensed under Apache License 2.0. That license applies only to the independently authored .NET wrapper.

Needle, Needle 3, model weights, native runtime components, Cactus technology, and other upstream artifacts retain their respective upstream licenses and copyright notices. The wrapper license does not relicense those works.

The NuGet package contains wrapper code only. At runtime, the default artifact provider obtains the native library and model independently from the official `Cactus-Compute/cactus` and `Cactus-Compute/needle3` distributions and caches it separately. It does not alter an upstream binary or its governing terms. Where an upstream distribution includes adjacent license or notice files, those files must remain intact; the wrapper's root `LICENSE` must never replace them.

Before every release, verify the licenses and notices attached to the current Needle repository, Needle 3 model, native runtime, and any other downloaded artifact. Update `THIRD_PARTY_NOTICES.md` if upstream terms or notices change, and inspect the packed NuGet archive to ensure no upstream artifact was included.

The artifact provider preserves license and notice files found inside the official runtime wheel under an `upstream-notices` subdirectory of the runtime cache. See `release-checklist.md` for the required release review.

The pinned upstream source, the engine distribution, and the Needle 3 model distribution identify Apache-2.0 as the governing license. The eight checked Cactus 4.1.4 runtime wheels contained no standalone license or notice members. The downloads remain subject to the upstream terms. [Pinned source license](https://github.com/cactus-compute/cactus/blob/3b0898b99d2479a674a453827f01d3c20099896f/LICENSE) · [Engine distribution](https://huggingface.co/Cactus-Compute/cactus) · [Model distribution](https://huggingface.co/Cactus-Compute/needle3)
