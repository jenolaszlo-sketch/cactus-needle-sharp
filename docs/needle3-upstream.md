# Needle 3 native migration assessment

WP-1 established a viable native path on Windows x64. PyPI listed `cactus-compute` 4.2.7 as its latest release on 2026-11-24. The official Git tag resolves to commit `3b0898b99d2479a674a453827f01d3c20099896f`. The tagged fetch code pins Cactus 4.1.4 at `f4ab2741203a282c6e7b89983de2b5e5b2e805a8` and `needle3.cact` at `d6c7d6a2e9655505e0f8e23339356dd1705900eb`. The 8,896-byte engine manifest hashes to `ab948753f08e54d2b24dd04830dcb984885524ab3bd1e9da3bf207276ab62eb6`. The 35,335,380-byte model hashes to `c9d915eca282ed42d1a09b143b592adb4cc6744ffe2d294adf5cfc5548170c38`. Both downloaded files matched. [Tagged fetch implementation](https://github.com/cactus-compute/cactus/blob/3b0898b99d2479a674a453827f01d3c20099896f/cactus/agent/fetch.py) · [Published release](https://pypi.org/project/cactus-compute/4.2.7/) · [Pinned manifest](https://huggingface.co/Cactus-Compute/cactus/resolve/f4ab2741203a282c6e7b89983de2b5e5b2e805a8/releases/4.1.4/manifest.json)

All eight wheels matched the manifest's sizes and digests and contained a shared library. The Windows x64 library loaded, resolved the six required exports, loaded the pinned model, returned a weather call and a no-call result, and repeated the sequence after reset and reinitialization. The other seven targets have artifact evidence and await host execution. The exact upstream engine manifest is retained alongside this assessment.

| ABI symbol | C signature |
|---|---|
| `cactus_load` | `int(const unsigned char*, unsigned long long)` |
| `cactus_init` | `int(const char*, const char*, const char*)` |
| `cactus_complete` | `int(const char*, const float*, int, int, char*, int)` |
| `cactus_reset` | `void(void)` |
| `cactus_last_error` | `const char*(void)` |
| `cactus_set_threads` | `void(int)` |

The pinned header and tagged binding agree. Calls use the C convention and UTF-8 NUL-terminated strings. Load receives bytes and their 64-bit length, and the upstream caller releases those bytes after load. Text completion passes a null audio pointer and zero samples. Negative returns indicate errors. The smoke test returned 36 on completion with a 363-byte JSON envelope, so the return value cannot serve as the output length. The response buffer is caller-owned. Copy the borrowed error string before another native operation. [Pinned header](https://huggingface.co/Cactus-Compute/cactus/resolve/f4ab2741203a282c6e7b89983de2b5e5b2e805a8/releases/4.1.4/windows-x86_64/cactus.h) · [Tagged ABI binding](https://github.com/cactus-compute/cactus/blob/3b0898b99d2479a674a453827f01d3c20099896f/cactus/agent/runtime.py)

Needle 3 uses little-endian archive tag `0x05e12a84`. Needle 2 uses `0x05e12a83`. The exporter describes a 196-byte header followed by a codebook and 44-byte tensor records. The upstream client isolates fine-tuned models in fresh processes and checks the archive's head manifest. A head can establish presence, while calibration for arbitrary caller-selected weights remains unknown without provenance. Retain exclusive in-process leases and retire workers that handle custom models. [Tagged exporter](https://github.com/cactus-compute/cactus/blob/3b0898b99d2479a674a453827f01d3c20099896f/cactus/model/export.py) · [Tagged text client](https://github.com/cactus-compute/cactus/blob/3b0898b99d2479a674a453827f01d3c20099896f/cactus/agent/text.py)

Native completion emits tool calls, confidence, and validation metadata on the tested request. The Python client adds date facts, tracks years across turns, annotates ungrounded values, and implements additional strict extraction and deterministic repairs. The .NET wrapper can retain schema validation and fail-closed call proposals without claiming parity with those Python behaviors. [Tagged grounding implementation](https://github.com/cactus-compute/cactus/blob/3b0898b99d2479a674a453827f01d3c20099896f/cactus/agent/grounding.py)
