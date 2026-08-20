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
11. Configure the NuGet.org trusted-publishing policy for owner `jenolaszlo-sketch`, repository `cactus-needle-sharp`, and workflow `publish-nuget.yml`. Set the GitHub repository secret `NUGET_USER` to the NuGet.org profile username, not an email address. Do not store a long-lived NuGet API key.
12. Manage versions as in Baize: update the single version in `Directory.Build.props` for ordinary/manual publishing, or push a `v<version>` tag (for example, `v0.1.0-alpha.1`) to publish that tag version without first editing the props file. A manual workflow run publishes the checked-in props version. The workflow tests, packs, audits, and publishes both packages and symbols; `--skip-duplicate` makes a repeated run safe.

Do not publish when an upstream artifact's governing terms are unclear.
