using System.Text.Json.Serialization.Metadata;

namespace CactusNeedleSharp;

/// <summary>Configures artifact resolution and inference defaults for <see cref="NeedleClient"/>.</summary>
public sealed record NeedleOptions
{
    /// <summary>Gets the directory used to cache downloaded native runtime artifacts.</summary>
    public string? CacheDirectory { get; init; }
    /// <summary>Gets an explicit native library path that bypasses download and caching.</summary>
    public string? NativeLibraryPath { get; init; }
    /// <summary>Gets the caller-supplied version label for an explicit native library; defaults to unknown.</summary>
    public string? ExplicitNativeLibraryVersion { get; init; }
    /// <summary>Gets the default custom weights path used when a session does not specify one.</summary>
    public string? ModelPath { get; init; }
    /// <summary>Gets whether network downloads are forbidden; requires an integrity-verified cached runtime.</summary>
    public bool Offline { get; init; }
    /// <summary>Gets whether artifact size, hash, and manifest integrity are verified; true by default.</summary>
    public bool VerifyArtifactIntegrity { get; init; } = true;
    /// <summary>Gets the expected SHA-256 of an explicit native library, verified when integrity checks apply.</summary>
    public string? ExpectedNativeLibrarySha256 { get; init; }
    /// <summary>Gets the native response buffer size in bytes.</summary>
    public int ResponseBufferSize { get; init; } = 65_536;
    /// <summary>Gets the default maximum number of new tokens per inference.</summary>
    public int DefaultMaxNewTokens { get; init; } = 256;
    /// <summary>Gets the default tool-index path passed to native initialization.</summary>
    public string? ToolIndexPath { get; init; }
}

/// <summary>Configures a single <see cref="INeedleSession"/>.</summary>
public sealed record NeedleSessionOptions
{
    /// <summary>Gets preformatted system facts text passed to native initialization.</summary>
    public string? SystemFacts { get; init; }
    /// <summary>Gets structured system facts formatted when <see cref="SystemFacts"/> is not set.</summary>
    public NeedleSystemFacts? Facts { get; init; }
    /// <summary>Gets the custom weights path; loading custom weights is a one-way door for the process.</summary>
    public string? WeightsPath { get; init; }
    /// <summary>Gets the tool-index path for this session, overriding the client default.</summary>
    public string? ToolIndexPath { get; init; }
}

/// <summary>Configures a single tool-call compilation.</summary>
public sealed record NeedleCompilationOptions
{
    /// <summary>Gets the maximum number of new tokens for this compilation.</summary>
    public int? MaxNewTokens { get; init; }
}

/// <summary>Configures a single structured extraction.</summary>
public sealed record NeedleExtractionOptions
{
    /// <summary>Gets the maximum number of new tokens for this extraction.</summary>
    public int? MaxNewTokens { get; init; }
    /// <summary>Gets the description used for the synthesized extraction tool.</summary>
    public string? Description { get; init; }
    /// <summary>Gets metadata for nested types referenced by source-generated extraction schemas.</summary>
    public Func<Type, JsonTypeInfo?>? NestedTypeResolver { get; init; }
}

/// <summary>Defines the minimum confidence required for a successful outcome.</summary>
public sealed record NeedleConfidencePolicy
{
    /// <summary>Gets the minimum confidence threshold; defaults to 0.80.</summary>
    public double MinimumConfidence { get; init; } = .80;
}

/// <summary>Identifies the resolved native runtime artifacts.</summary>
/// <param name="NativeLibraryPath">The full path to the native library.</param>
/// <param name="Version">The engine version the artifacts belong to.</param>
/// <param name="Source">The artifact source, such as explicit path or repository name.</param>
public sealed record NeedleArtifacts(string NativeLibraryPath, string Version, string Source);

/// <summary>Describes wrapper, runtime, and model versions for a client.</summary>
public sealed record NeedleRuntimeInfo
{
    /// <summary>Gets the managed wrapper version.</summary>
    public string? WrapperVersion { get; init; }
    /// <summary>Gets the native runtime version.</summary>
    public string? RuntimeVersion { get; init; }
    /// <summary>Gets the model version.</summary>
    public string? ModelVersion { get; init; }
    /// <summary>Gets the model or artifact source.</summary>
    public string? ModelSource { get; init; }
}

/// <summary>Reports timing and throughput for one inference.</summary>
public sealed record NeedleInferenceMetrics
{
    /// <summary>Gets how long inference took.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Gets prefill throughput in tokens per second, if reported.</summary>
    public double? PrefillTokensPerSecond { get; init; }
    /// <summary>Gets decode throughput in tokens per second, if reported.</summary>
    public double? DecodeTokensPerSecond { get; init; }
}
