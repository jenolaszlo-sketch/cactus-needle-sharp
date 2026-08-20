# Release licensing checklist

Before every release:

1. Check the current license in the official Cactus Compute Needle repository.
2. Check the current Needle 2 model-distribution license.
3. Check the license and notices attached to each native runtime artifact consumed by the wrapper.
4. Check whether upstream added or changed a `NOTICE` or third-party notice file.
5. Check whether redistribution or download requirements changed.
6. Update `THIRD_PARTY_NOTICES.md` without paraphrasing away required notices.
7. Run `dotnet pack` and `eng/Verify-NuGetPackage.ps1` against the resulting archive.
8. Confirm the package contains the wrapper README, Apache-2.0 license, NOTICE, and third-party notices.
9. Confirm the package contains no model weights, native runtime, wheel, executable, or static library.
10. Confirm the README and NuGet metadata still describe CactusNeedleSharp as an unofficial independent wrapper.
11. Set the GitHub repository secret `NUGET_API_KEY` to a NuGet.org API key scoped only to the `CactusNeedleSharp` and `CactusNeedleSharp.Worker` packages.
12. Update the single version in `Directory.Build.props`, then publish a GitHub Release whose tag is exactly `v<version>` (for example, `v0.1.0-alpha.1`). The release workflow validates, tests, packs, audits, attaches, and publishes both packages and symbols.

Do not publish when an upstream artifact's governing terms are unclear.
