using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CactusNeedleSharp;

internal static partial class NeedleNative
{
    private const string LibraryName = "CactusNeedleSharp.Native";
    private static readonly object Sync = new();
    private static IntPtr _handle;
    private static string? _loadedPath;
    private static string? _loadedHash;
    private static readonly string[] RequiredExports =
        ["cactus_load", "cactus_init", "cactus_complete", "cactus_reset", "cactus_last_error", "cactus_set_threads"];

    static NeedleNative() => NativeLibrary.SetDllImportResolver(typeof(NeedleNative).Assembly, Resolve);

    internal static void Load(NeedleArtifacts artifact)
    {
        var fullPath = Path.GetFullPath(artifact.NativeLibraryPath);
        lock (Sync)
        {
            if (_handle != IntPtr.Zero)
            {
                if (_loadedHash is not null && _loadedHash == artifact.Sha256) return;
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (_loadedHash is null && artifact.Sha256 is null && string.Equals(_loadedPath, fullPath, comparison)) return;
                throw new NeedleNativeLibraryException($"Cactus is already loaded from '{_loadedPath}'. Use an isolated worker for '{fullPath}'.");
            }

            IntPtr handle;
            try { handle = NativeLibrary.Load(fullPath); }
            catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or FileLoadException)
            { throw new NeedleNativeLibraryException($"Unable to load the Cactus runtime at '{fullPath}'.", error); }

            foreach (var symbol in RequiredExports)
            {
                if (NativeLibrary.TryGetExport(handle, symbol, out _)) continue;
                NativeLibrary.Free(handle);
                throw new NeedleNativeLibraryException($"Library '{fullPath}' lacks the required Cactus 4 export '{symbol}'.");
            }

            _loadedPath = fullPath;
            _loadedHash = artifact.Sha256;
            _handle = handle;
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path) =>
        name == LibraryName ? _handle : IntPtr.Zero;

    internal static string? LastError() => Marshal.PtrToStringUTF8(LastErrorPointer());

    [LibraryImport(LibraryName, EntryPoint = "cactus_init", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Init(string? systemFacts, string toolsJson, string? toolIndexPath);

    [LibraryImport(LibraryName, EntryPoint = "cactus_complete", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Complete(string input, float* audio, int samples,
        int maxNewTokens, byte* output, int outputCapacity);

    [LibraryImport(LibraryName, EntryPoint = "cactus_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Reset();

    [LibraryImport(LibraryName, EntryPoint = "cactus_last_error")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr LastErrorPointer();

    [LibraryImport(LibraryName, EntryPoint = "cactus_set_threads")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetThreads(int threads);

    [LibraryImport(LibraryName, EntryPoint = "cactus_load")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int LoadWeights(byte* cact, ulong length);
}
