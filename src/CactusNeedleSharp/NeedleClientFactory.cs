namespace CactusNeedleSharp;

/// <summary>Default <see cref="INeedleClientFactory"/> that builds clients from fixed options and artifacts.</summary>
public sealed class NeedleClientFactory : INeedleClientFactory
{
    private readonly NeedleOptions _options;
    private readonly INeedleArtifactProvider _artifacts;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    /// <summary>Initializes the factory with client options, an artifact provider, and an optional logger factory.</summary>
    public NeedleClientFactory(NeedleOptions options, INeedleArtifactProvider artifacts,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
    {
        _options = options;
        _artifacts = artifacts;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Creates and initializes a <see cref="NeedleClient"/> from the configured options.</summary>
    public ValueTask<NeedleClient> CreateAsync(CancellationToken cancellationToken = default) =>
        NeedleClient.CreateAsync(_options, _artifacts, _loggerFactory, cancellationToken);
}
