# Architecture

`NeedleClient` implements local compilation, planning, session creation, and structured extraction. Tools use JSON Schema and arguments use `JsonElement`. The wrapper produces proposals and the application controls execution.

The [artifact provider](native-runtime.md) resolves a verified Cactus shared library and Needle 3 model separately. `NeedleModelManager.EnsureAvailableAsync` can prefetch both through the default provider. `NeedleNative` binds the process-global native ABI and owns the library handle for the process lifetime.

An in-process session owns the runtime lease and serializes access. The first loaded model remains resident across sessions, and session disposal resets conversation state before releasing the lease. Independent concurrent conversations and model transitions use the [worker pool](worker-pool.md).

The managed response path decodes native envelopes with source-generated JSON metadata. Native suppressions and managed rejections remain visible for diagnosis. The default outcome policy blocks the entire proposal set for suppression, negation, flagged grounding, schema or tool-name violations, or unknown confidence. Application authorization and domain validation still apply to candidates that pass the policy.

The package's argument verifier covers the generated schema vocabulary and common object, array, type, enum, numeric and string assertions. Schema keywords the verifier cannot evaluate, including references and formats, make the affected proposal ineligible for a successful outcome. Use source-generated `JsonTypeInfo<T>` overloads for NativeAOT.
