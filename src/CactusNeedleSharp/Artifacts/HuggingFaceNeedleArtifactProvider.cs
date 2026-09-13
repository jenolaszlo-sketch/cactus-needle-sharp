using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CactusNeedleSharp;

/// <summary>Resolves the official Needle runtime from explicit paths, cache, or Hugging Face downloads.</summary>
public sealed class HuggingFaceNeedleArtifactProvider : INeedleArtifactProvider, IDisposable
{
    /// <summary>Gets the pinned Needle engine version this provider installs.</summary>
    public const string EngineVersion = "2.0.3";
    private const string Repository = "Cactus-Compute/needle2";
    private const long MaxUnverifiedWheelBytes = 256L * 1024 * 1024;
    private readonly NeedleOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;
    private static readonly IReadOnlyDictionary<string, ArtifactDescriptor> Artifacts =
        new Dictionary<string, ArtifactDescriptor>(StringComparer.Ordinal)
        {
            ["macosx_11_0_arm64"] = new(13_322_818, "17c2b9ff3c3f1238e0a26385cfda0780d120cda390594d7fc7e5b7f2a970ce95"),
            ["macosx_11_0_x86_64"] = new(13_217_044, "dc55a60b6803fbfd73fa50c09803df54bb47155dcdec74e5988c21838d5cc070"),
            ["manylinux2014_aarch64"] = new(13_289_275, "0e6f0d04e42ac16f34661c7eaab027c87e1fdac294b3dbdb6ca5c9d0597398ab"),
            ["manylinux2014_x86_64"] = new(13_283_220, "d23df1d0babeb7323dcaf860dfaf833bbd7d2229b205f691c05c9cbc6d3d3653"),
            ["win_amd64"] = new(13_299_800, "3c012603a6bc5d7f36aa26da3d0819a8fa226dd40c7f242013b5e214a51168c7"),
            ["win_arm64"] = new(13_268_140, "cadcd8ff7f18b47046c547cbc450dabe607c197db2855eb6497d615ff551db0f")
        };

    /// <summary>Initializes the provider with options and an optional shared HTTP client.</summary>
    public HuggingFaceNeedleArtifactProvider(NeedleOptions? options = null, HttpClient? httpClient = null)
    { _options = options ?? new(); _ownsHttpClient = httpClient is null; _httpClient = httpClient ?? new HttpClient(); }

    /// <summary>Returns cached or downloaded artifacts, honoring explicit paths, offline mode, and integrity checks.</summary>
    public async ValueTask<NeedleArtifacts> GetArtifactsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var explicitPath = _options.NativeLibraryPath ?? Environment.GetEnvironmentVariable("NEEDLE_LIB_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath)) throw new NeedleArtifactNotFoundException($"Needle native library was not found at '{explicitPath}'.");
            if (_options.VerifyArtifactIntegrity && !string.IsNullOrWhiteSpace(_options.ExpectedNativeLibrarySha256))
                await VerifyFileAsync(explicitPath, _options.ExpectedNativeLibrarySha256, cancellationToken).ConfigureAwait(false);
            var explicitVersion = string.IsNullOrWhiteSpace(_options.ExplicitNativeLibraryVersion)
                ? "unknown"
                : _options.ExplicitNativeLibraryVersion;
            return new(Path.GetFullPath(explicitPath), explicitVersion, "explicit");
        }

        var cache = Path.GetFullPath(_options.CacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CactusNeedleSharp", EngineVersion));
        var libraryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libneedle.dll" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libneedle.dylib" : "libneedle.so";
        var libraryPath = Path.Combine(cache, libraryName);
        var tag = GetPythonPlatformTag();
        var wheel = $"cactus_needle-{EngineVersion}-py3-none-{tag}.whl";
        var descriptor = Artifacts[tag];
        var manifestPath = Path.Combine(cache, "artifact-manifest.json");
        if (File.Exists(libraryPath) && await IsCacheValidAsync(libraryPath, manifestPath, wheel, descriptor, cancellationToken).ConfigureAwait(false))
            return new(libraryPath, EngineVersion, Repository);
        if (_options.Offline) throw new NeedleArtifactNotFoundException($"Offline mode is enabled and no integrity-verified Needle runtime is available at '{libraryPath}'.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cache);
            await using var cacheLock = await AcquireCacheLockAsync(cache, cancellationToken).ConfigureAwait(false);
            if (File.Exists(libraryPath) && await IsCacheValidAsync(libraryPath, manifestPath, wheel, descriptor, cancellationToken).ConfigureAwait(false))
                return new(libraryPath, EngineVersion, Repository);
            var url = $"https://huggingface.co/{Repository}/resolve/main/python/{wheel}?download=true";
            var temporary = Path.Combine(cache, $".{wheel}.{Guid.NewGuid():N}.tmp");
            string? extracted = null;
            string? manifestTemporary = null;
            try
            {
                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
                    // Enforce the size cap while streaming so a hostile origin
                    // cannot fill the disk before the post-download check.
                    var limit = _options.VerifyArtifactIntegrity ? descriptor.Size : MaxUnverifiedWheelBytes;
                    var buffer = new byte[81920];
                    long received = 0;
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (read == 0) break;
                        received += read;
                        if (received > limit)
                            throw new NeedleArtifactException($"Needle runtime download exceeded {limit} bytes.");
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                }
                if (_options.VerifyArtifactIntegrity)
                {
                    var wheelInfo = new FileInfo(temporary);
                    if (wheelInfo.Length != descriptor.Size)
                        throw new NeedleArtifactException($"Needle runtime wheel size mismatch: expected {descriptor.Size}, received {wheelInfo.Length} bytes.");
                    await VerifyFileAsync(temporary, descriptor.Sha256, cancellationToken).ConfigureAwait(false);
                }
                using var archive = ZipFile.OpenRead(temporary);
                var entry = archive.GetEntry($"needle/{libraryName}") ?? throw new NeedleArtifactException($"Official wheel did not contain needle/{libraryName}.");
                extracted = libraryPath + $".{Guid.NewGuid():N}.tmp";
                entry.ExtractToFile(extracted);
                var nativeSha256 = await ComputeSha256Async(extracted, cancellationToken).ConfigureAwait(false);
                File.Move(extracted, libraryPath, true);
                extracted = null;
                ExtractUpstreamNotices(archive, cache);
                var manifest = new ArtifactManifest(EngineVersion, wheel, descriptor.Sha256, nativeSha256, url);
                manifestTemporary = manifestPath + $".{Guid.NewGuid():N}.tmp";
                await File.WriteAllTextAsync(manifestTemporary, JsonSerializer.Serialize(manifest, NeedleJsonContext.Default.ArtifactManifest), cancellationToken).ConfigureAwait(false);
                File.Move(manifestTemporary, manifestPath, true);
                manifestTemporary = null;
            }
            catch (OperationCanceledException) { throw; }
            catch (NeedleArtifactException) { throw; }
            catch (Exception exception) { throw new NeedleArtifactException("Failed to download the official Needle runtime artifact.", exception); }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (extracted is not null && File.Exists(extracted)) File.Delete(extracted);
                if (manifestTemporary is not null && File.Exists(manifestTemporary)) File.Delete(manifestTemporary);
            }
            return new(libraryPath, EngineVersion, Repository);
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<bool> IsCacheValidAsync(string libraryPath, string manifestPath, string wheel,
        ArtifactDescriptor descriptor, CancellationToken cancellationToken)
    {
        if (!_options.VerifyArtifactIntegrity) return true;
        if (!File.Exists(manifestPath)) return false;
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize(json, NeedleJsonContext.Default.ArtifactManifest);
            if (manifest is null || manifest.Version != EngineVersion || manifest.WheelFile != wheel ||
                !string.Equals(manifest.WheelSha256, descriptor.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
            var actual = await ComputeSha256Async(libraryPath, cancellationToken).ConfigureAwait(false);
            return string.Equals(actual, manifest.NativeLibrarySha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { return false; }
    }

    internal static async ValueTask VerifyFileAsync(string path, string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (expectedSha256.Length != 64 || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Expected SHA-256 must contain exactly 64 hexadecimal characters.", nameof(expectedSha256));
        var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new NeedleArtifactException($"Artifact SHA-256 mismatch for '{path}': expected {expectedSha256}, received {actual}.");
    }

    private static async ValueTask<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static async ValueTask<FileStream> AcquireCacheLockAsync(string cacheDirectory, CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(cacheDirectory, ".artifact-install.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.Asynchronous);
            }
            catch (IOException exception) when (IsLockContention(exception))
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsLockContention(IOException exception) =>
        exception.HResult == unchecked((int)0x80070020) || exception.HResult == unchecked((int)0x80070021);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _gate.Dispose();
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    internal static void ExtractUpstreamNotices(ZipArchive archive, string cacheDirectory)
    {
        var noticeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "LICENSE", "LICENSE.txt", "NOTICE", "NOTICE.txt", "COPYING", "THIRD_PARTY_NOTICES.md" };
        var entries = archive.Entries.Where(entry => noticeNames.Contains(Path.GetFileName(entry.FullName))).ToArray();
        if (entries.Length == 0) return;

        var noticesDirectory = Path.Combine(cacheDirectory, "upstream-notices");
        Directory.CreateDirectory(noticesDirectory);
        foreach (var notice in entries)
        {
            var safeName = notice.FullName.Replace('/', '_').Replace('\\', '_');
            var destination = Path.Combine(noticesDirectory, safeName);
            var temporary = destination + $".{Guid.NewGuid():N}.tmp";
            notice.ExtractToFile(temporary);
            File.Move(temporary, destination, true);
        }
    }

    internal static string GetPythonPlatformTag()
    {
        var arm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var x64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;
        if (!arm64 && !x64) throw new NeedleArtifactException($"Unsupported process architecture: {RuntimeInformation.ProcessArchitecture}.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return arm64 ? "win_arm64" : "win_amd64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return arm64 ? "macosx_11_0_arm64" : "macosx_11_0_x86_64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return arm64 ? "manylinux2014_aarch64" : "manylinux2014_x86_64";
        throw new NeedleArtifactException($"Unsupported platform: {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}.");
    }
}

internal sealed record ArtifactDescriptor(long Size, string Sha256);
internal sealed record ArtifactManifest(string Version, string WheelFile, string WheelSha256, string NativeLibrarySha256, string SourceUrl);

/// <summary>Ensures the Needle runtime is available via the configured artifact provider.</summary>
public sealed class NeedleModelManager
{
    private readonly INeedleArtifactProvider _provider;
    /// <summary>Initializes the manager with the underlying artifact provider.</summary>
    public NeedleModelManager(INeedleArtifactProvider provider) => _provider = provider;
    /// <summary>Returns the resolved native artifacts.</summary>
    public ValueTask<NeedleArtifacts> GetArtifactsAsync(CancellationToken cancellationToken = default) => _provider.GetArtifactsAsync(cancellationToken);
    /// <summary>Ensures the runtime is downloaded and verified, returning the resolved artifacts.</summary>
    public ValueTask<NeedleArtifacts> EnsureAvailableAsync(CancellationToken cancellationToken = default) => _provider.GetArtifactsAsync(cancellationToken);
}
