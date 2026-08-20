using CactusNeedleSharp;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CactusNeedleSharp.Tests;

public sealed class ArtifactTests
{
    [Fact]
    public async Task OfflineMissingArtifactNamesExpectedPath()
    {
        var cache = Path.Combine(Path.GetTempPath(), "needle-tests", Guid.NewGuid().ToString("N"));
        var provider = new HuggingFaceNeedleArtifactProvider(new() { CacheDirectory = cache, Offline = true });
        var exception = await Assert.ThrowsAsync<NeedleArtifactNotFoundException>(() => provider.GetArtifactsAsync().AsTask());
        Assert.Contains(cache, exception.Message);
    }

    [Fact]
    public void SystemFactsUseUpstreamFormat()
    {
        var text = new NeedleSystemFacts { Locale = "en-US", Device = "desktop", Raw = new Dictionary<string, string> { ["custom"] = "value" } }.ToString();
        Assert.Equal("locale: en-US; device: desktop; custom: value", text);
    }

    [Fact]
    public void OfficialWheelNoticesArePreservedUnmodified()
    {
        var root = Path.Combine(Path.GetTempPath(), "needle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wheelPath = Path.Combine(root, "runtime.whl");
        using (var wheel = ZipFile.Open(wheelPath, ZipArchiveMode.Create))
        {
            var entry = wheel.CreateEntry("cactus_needle-2.0.3.dist-info/licenses/LICENSE");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("authoritative upstream license\n");
        }

        using (var wheel = ZipFile.OpenRead(wheelPath))
            HuggingFaceNeedleArtifactProvider.ExtractUpstreamNotices(wheel, root);

        var preserved = Directory.GetFiles(Path.Combine(root, "upstream-notices"), "*").Single();
        Assert.Equal("authoritative upstream license\n", File.ReadAllText(preserved).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task ArtifactChecksumRejectsTampering()
    {
        var path = Path.Combine(Path.GetTempPath(), $"needle-checksum-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, "verified");
        try
        {
            var expected = Convert.ToHexString(SHA256.HashData("verified"u8.ToArray())).ToLowerInvariant();
            await HuggingFaceNeedleArtifactProvider.VerifyFileAsync(path, expected);
            await File.AppendAllTextAsync(path, "tampered");
            await Assert.ThrowsAsync<NeedleArtifactException>(() =>
                HuggingFaceNeedleArtifactProvider.VerifyFileAsync(path, expected).AsTask());
        }
        finally { File.Delete(path); }
    }
}
