# Native runtime

Resolution order is `NeedleOptions.NativeLibraryPath`, upstream `NEEDLE_LIB_PATH`, and the wrapper cache. Set `NeedleOptions.Offline = true` to guarantee that a missing artifact fails without network access.

The default cache is `%LOCALAPPDATA%/CactusNeedleSharp/2.0.3` on Windows and the corresponding .NET local-application-data folder elsewhere. `CacheDirectory` overrides it.

The official ABI has no interruption function. Cancellation is observed before and after inference, but native compute may run to completion internally.
