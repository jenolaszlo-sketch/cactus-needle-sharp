using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CactusNeedleSharp;

/// <summary>Resolves verified Cactus engines and independently versioned Needle weights.</summary>
public sealed class HuggingFaceNeedleArtifactProvider : INeedleArtifactProvider, IDisposable
{
    /// <summary>Gets the pinned Cactus engine version.</summary>
    public const string EngineVersion = "4.1.4";
    internal const string EngineRepository = "Cactus-Compute/cactus";
    internal const string EngineRevision = "f4ab2741203a282c6e7b89983de2b5e5b2e805a8";
    internal const string ModelRepository = "Cactus-Compute/needle3";
    internal const string ModelRevision = "d6c7d6a2e9655505e0f8e23339356dd1705900eb";
    internal const string ModelName = "needle3.cact";

    private static readonly ArtifactDescriptor Model = new(35335380, "c9d915eca282ed42d1a09b143b592adb4cc6744ffe2d294adf5cfc5548170c38");
    private static readonly IReadOnlyDictionary<string, EngineArtifact> Engines =
        new Dictionary<string, EngineArtifact>(StringComparer.Ordinal)
        {
            ["macosx_11_0_arm64"] = new(new(745539, "45eaae74b37291425bc8bedfc3fa0ff6984cf8ea1997d2ea1711387974d25e43"), new(1451664, "f28e1f4f7792a4649b4e91d94f9a17f0c037f41a19b4b848f2f47ba960231ddd")),
            ["macosx_11_0_x86_64"] = new(new(808075, "bb04050273486582ef8484bd5c95cba3a43cb2db4510970b1f97cc6432f464cd"), new(1689112, "09d6491861aa7d1025defce2fbeb551a66764c3ba24e7e3e3808c4fd9003c10e")),
            ["manylinux2014_aarch64"] = new(new(847738, "45b703ee71fd979d3155f945a733c1dd1f18637035314e9c96fdfd60ae96b787"), new(1830632, "4ce9d02bb35cf40f5c60965598d4b9556c956379cebe65187b936430a4b56c17")),
            ["manylinux2014_x86_64"] = new(new(881626, "314cd027e95cae1cb8537c478ca6cddaec3fd02165f80d960f1934399871fc33"), new(1981624, "6c677ec261ab8f50802e8d9c261cd5fbfa0a7be35cef3d90b62b4f80f4125715")),
            ["musllinux_1_2_aarch64"] = new(new(847733, "f54163ee24baf7a5fef6e864bd440d3e6e87363932ed30ac56f676507e57d5bf"), new(1834232, "08113140f00d662c88d8e39be7a72adc07b23d139048b90d1939a230575632db")),
            ["musllinux_1_2_x86_64"] = new(new(882518, "78fd9d4ec5d1c091fd354afc6a0c4714b5fa1a3a2dda95019adc421374495e40"), new(1986728, "fda663dd0bba027e6d5f80d7326f31f4df4015ca3b76b0dc3a63361d046d6aa9")),
            ["win_amd64"] = new(new(891053, "76f21737d93a5246afe2bfee65a5a338df35217cb6258c7aa9cc747e6ea2bb73"), new(1914880, "a28490bc7826c7b0ee9c51bc3092a26ec935a85d08ea82a7ed3593fadde5b381")),
            ["win_arm64"] = new(new(809059, "f8dae7aacb3912623814db25c2d336b5b2a1b00fb1eb96d630ef53899b156e07"), new(1646080, "a21029b12ea6389c18c9d799e51599910bb7546814fef70e1d7a1e841c4610aa")),
        };

    private readonly NeedleOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private int _disposed;

    /// <summary>Initializes the provider with options and an optional shared HTTP client.</summary>
    public HuggingFaceNeedleArtifactProvider(NeedleOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new();
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
    }

    private string CacheRoot => Path.GetFullPath(_options.CacheDirectory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CactusNeedleSharp"));

    /// <summary>Resolves the native engine, independently of the model location.</summary>
    public async ValueTask<NeedleArtifacts> GetArtifactsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var explicitPath = _options.NativeLibraryPath ?? Environment.GetEnvironmentVariable("NEEDLE_LIB_PATH") ??
            Environment.GetEnvironmentVariable("CACTUS_LIB_PATH");
        var tag = GetPythonPlatformTag();
        var engine = Engines[tag];
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var path = Path.GetFullPath(explicitPath);
            if (!File.Exists(path))
                throw new NeedleArtifactNotFoundException($"Cactus native library was not found at '{path}'.");
            var hash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            var expected = _options.ExpectedNativeLibrarySha256;
            if (expected is not null && !string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
                throw new NeedleArtifactException($"Configured Cactus library SHA-256 mismatch at '{path}'.");
            if (hash == engine.Library.Sha256)
                return OfficialEngine(path, hash);
            return new(path, string.IsNullOrWhiteSpace(_options.ExplicitNativeLibraryVersion) ? "unknown" : _options.ExplicitNativeLibraryVersion, "explicit")
            { Sha256 = hash, IntegrityVerified = expected is not null };
        }

        var cache = Path.Combine(CacheRoot, "engine", EngineVersion, EngineRevision, tag);
        var name = OperatingSystem.IsWindows() ? "libcactus.dll" : OperatingSystem.IsMacOS() ? "libcactus.dylib" : "libcactus.so";
        var libraryPath = Path.Combine(cache, name);
        if (await IsValidAsync(libraryPath, engine.Library, cancellationToken).ConfigureAwait(false))
            return OfficialEngine(libraryPath, engine.Library.Sha256);
        RequireOnline(libraryPath);

        Directory.CreateDirectory(cache);
        await using var cacheLock = await AcquireCacheLockAsync(cache, cancellationToken).ConfigureAwait(false);
        if (await IsValidAsync(libraryPath, engine.Library, cancellationToken).ConfigureAwait(false))
            return OfficialEngine(libraryPath, engine.Library.Sha256);

        var wheel = $"cactus_runtime-{EngineVersion}-py3-none-{tag}.whl";
        var url = $"https://huggingface.co/{EngineRepository}/resolve/{EngineRevision}/releases/{EngineVersion}/python/{wheel}";
        var wheelTemp = TemporaryPath(libraryPath);
        var libraryTemp = TemporaryPath(libraryPath);
        try
        {
            await DownloadAsync(url, wheelTemp, engine.Wheel, cancellationToken).ConfigureAwait(false);
            using var archive = ZipFile.OpenRead(wheelTemp);
            var entry = archive.GetEntry($"cactus/{name}");
            if (entry is null || entry.Length != engine.Library.Size)
                throw new NeedleArtifactException($"Invalid shared-library entry cactus/{name} in the pinned wheel.");
            entry.ExtractToFile(libraryTemp);
            await VerifyFileAsync(libraryTemp, engine.Library.Sha256, cancellationToken).ConfigureAwait(false);
            ExtractUpstreamNotices(archive, cache);
            File.Move(libraryTemp, libraryPath, true);
            return OfficialEngine(libraryPath, engine.Library.Sha256);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new NeedleArtifactException("Unable to install the pinned Cactus shared library.", error);
        }
        finally
        {
            File.Delete(wheelTemp);
            File.Delete(libraryTemp);
        }
    }

    /// <summary>Resolves the configured model or the official Needle 3 weights.</summary>
    public ValueTask<NeedleModelArtifact> GetModelArtifactsAsync(CancellationToken cancellationToken = default) =>
        GetModelArtifactsAsync(null, cancellationToken);

    internal async ValueTask<NeedleModelArtifact> GetModelArtifactsAsync(string? overridePath, CancellationToken cancellationToken, string? expectedOverride = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = overridePath ?? _options.ModelPath;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            var path = Path.GetFullPath(selected);
            if (!File.Exists(path))
                throw new NeedleArtifactNotFoundException($"Needle model was not found at '{path}'.");
            var metadata = NeedleModelFormat.Read(path);
            var hash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            var expected = overridePath is null ? _options.ExpectedModelSha256 : expectedOverride;
            if (expected is not null && !string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
                throw new NeedleArtifactException($"Configured Needle model SHA-256 mismatch at '{path}'.");
            if (hash == Model.Sha256)
                return OfficialModel(path);
            return new(path, "explicit", null, hash, expected is not null, metadata, null);
        }

        var cache = Path.Combine(CacheRoot, "models", "needle3", ModelRevision);
        var modelPath = Path.Combine(cache, ModelName);
        if (await IsValidAsync(modelPath, Model, cancellationToken).ConfigureAwait(false))
            return OfficialModel(modelPath);
        RequireOnline(modelPath);

        Directory.CreateDirectory(cache);
        await using var cacheLock = await AcquireCacheLockAsync(cache, cancellationToken).ConfigureAwait(false);
        if (!await IsValidAsync(modelPath, Model, cancellationToken).ConfigureAwait(false))
        {
            var temporary = TemporaryPath(modelPath);
            try
            {
                await DownloadAsync($"https://huggingface.co/{ModelRepository}/resolve/{ModelRevision}/{ModelName}",
                    temporary, Model, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, modelPath, true);
            }
            finally { File.Delete(temporary); }
        }
        return OfficialModel(modelPath);
    }

    private static NeedleArtifacts OfficialEngine(string path, string hash) =>
        new(path, EngineVersion, EngineRepository) { Revision = EngineRevision, Sha256 = hash, IntegrityVerified = true };

    private static NeedleModelArtifact OfficialModel(string path) =>
        new(path, ModelRepository, ModelRevision, Model.Sha256, true, true, true);

    private void RequireOnline(string path)
    {
        if (_options.Offline)
            throw new NeedleArtifactNotFoundException($"Offline mode requires an integrity-verified artifact at '{path}'.");
    }

    private static string TemporaryPath(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    private static async ValueTask<bool> IsValidAsync(string path, ArtifactDescriptor descriptor, CancellationToken token)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length == descriptor.Size &&
                await ComputeSha256Async(path, token).ConfigureAwait(false) == descriptor.Sha256;
        }
        catch (FileNotFoundException) { return false; }
    }

    private async Task DownloadAsync(string url, string path, ArtifactDescriptor descriptor, CancellationToken token)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    received += count;
                    if (received > descriptor.Size)
                        throw new NeedleArtifactException($"Artifact download exceeded {descriptor.Size} bytes.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                if (received != descriptor.Size)
                    throw new NeedleArtifactException($"Artifact size mismatch, expected {descriptor.Size} bytes and received {received}.");
            }
            await VerifyFileAsync(path, descriptor.Sha256, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            throw new NeedleArtifactException($"Failed downloading pinned artifact '{url}'.", error);
        }
    }

    internal static async ValueTask VerifyFileAsync(string path, string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (expectedSha256.Length != 64 || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Expected SHA-256 must contain exactly 64 hexadecimal characters.", nameof(expectedSha256));
        var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new NeedleArtifactException($"Artifact SHA-256 mismatch for '{path}', expected {expectedSha256} and received {actual}.");
    }

    private static async ValueTask<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static async ValueTask<FileStream> AcquireCacheLockAsync(string directory, CancellationToken token)
    {
        var path = Path.Combine(directory, ".artifact-install.lock");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when (error.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) or 11 or 35)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsHttpClient) _httpClient.Dispose();
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
        if (OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid())
        {
            const string maps = "/proc/self/maps";
            var family = RuntimeInformation.RuntimeIdentifier.StartsWith("linux-musl", StringComparison.Ordinal) ||
                (File.Exists(maps) && File.ReadLines(maps).Any(line => line.Contains("/ld-musl-", StringComparison.Ordinal)))
                ? "musllinux_1_2_" : "manylinux2014_";
            return family + (arm64 ? "aarch64" : "x86_64");
        }
        throw new NeedleArtifactException($"Unsupported platform: {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}.");
    }
}

internal sealed record ArtifactDescriptor(long Size, string Sha256);
internal sealed record EngineArtifact(ArtifactDescriptor Wheel, ArtifactDescriptor Library);

/// <summary>Ensures the Needle runtime is available via the configured artifact provider.</summary>
public sealed class NeedleModelManager
{
    private readonly INeedleArtifactProvider _provider;
    /// <summary>Initializes the manager with the underlying artifact provider.</summary>
    public NeedleModelManager(INeedleArtifactProvider provider) => _provider = provider;
    /// <summary>Returns the resolved native artifacts.</summary>
    public ValueTask<NeedleArtifacts> GetArtifactsAsync(CancellationToken cancellationToken = default) => _provider.GetArtifactsAsync(cancellationToken);
    /// <summary>Ensures the runtime is available and resolves the model when using the default provider.</summary>
    public async ValueTask<NeedleArtifacts> EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        var engine = await _provider.GetArtifactsAsync(cancellationToken).ConfigureAwait(false);
        if (_provider is HuggingFaceNeedleArtifactProvider official)
            await official.GetModelArtifactsAsync(cancellationToken).ConfigureAwait(false);
        return engine;
    }
}
