namespace CactusNeedleSharp;

public sealed record NeedleOptions
{
    public string? CacheDirectory { get; init; }
    public string? NativeLibraryPath { get; init; }
    public string? ModelPath { get; init; }
    public bool Offline { get; init; }
    public int ResponseBufferSize { get; init; } = 65_536;
    public int DefaultMaxNewTokens { get; init; } = 256;
    public string? ToolIndexPath { get; init; }
}

public sealed record NeedleSessionOptions
{
    public string? SystemFacts { get; init; }
    public NeedleSystemFacts? Facts { get; init; }
    public string? WeightsPath { get; init; }
    public string? ToolIndexPath { get; init; }
}

public sealed record NeedleCompilationOptions { public int? MaxNewTokens { get; init; } }
public sealed record NeedleExtractionOptions { public int? MaxNewTokens { get; init; } public string? Description { get; init; } }
public sealed record NeedleConfidencePolicy { public double MinimumConfidence { get; init; } = .80; }
public sealed record NeedleArtifacts(string NativeLibraryPath, string Version, string Source);
public sealed record NeedleRuntimeInfo { public string? WrapperVersion { get; init; } public string? RuntimeVersion { get; init; } public string? ModelVersion { get; init; } public string? ModelSource { get; init; } }
public sealed record NeedleInferenceMetrics { public TimeSpan Duration { get; init; } public double? PrefillTokensPerSecond { get; init; } public double? DecodeTokensPerSecond { get; init; } }
