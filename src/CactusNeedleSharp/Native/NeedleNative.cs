using System.Reflection;
using System.Runtime.InteropServices;

namespace CactusNeedleSharp;

internal static partial class NeedleNative
{
    internal const string LibraryName = "CactusNeedleSharp.Native";
    private static IntPtr _handle;
    private static string? _loadedPath;

    static NeedleNative() => NativeLibrary.SetDllImportResolver(typeof(NeedleNative).Assembly, Resolve);

    internal static void Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_handle != IntPtr.Zero)
        {
            if (!string.Equals(_loadedPath, fullPath, StringComparison.OrdinalIgnoreCase))
                throw new NeedleNativeLibraryException($"Needle is already loaded from '{_loadedPath}' and cannot be rebound in this process to '{fullPath}'. Use an isolated worker process for another runtime.");
            return;
        }
        try { _handle = NativeLibrary.Load(fullPath); _loadedPath = fullPath; }
        catch (Exception exception) { throw new NeedleNativeLibraryException($"Unable to load the Needle runtime at '{path}'.", exception); }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path) =>
        name == LibraryName && _handle != IntPtr.Zero ? _handle : IntPtr.Zero;

    [LibraryImport(LibraryName, EntryPoint = "needle_init", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Init(string? systemFacts, string toolsJson, string? toolIndexPath);

    [LibraryImport(LibraryName, EntryPoint = "needle_complete", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Complete(string input, int maxNewTokens, byte[] output, int outputCapacity);

    [LibraryImport(LibraryName, EntryPoint = "needle_reset")]
    internal static partial void Reset();

    [LibraryImport(LibraryName, EntryPoint = "needle_load")]
    internal static unsafe partial int LoadWeights(byte* cact, ulong length);
}
