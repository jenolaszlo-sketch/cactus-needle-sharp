namespace CactusNeedleSharp;

public sealed class NeedleClientFactory : INeedleClientFactory
{
    private readonly NeedleOptions _options;
    private readonly INeedleArtifactProvider _artifacts;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    public NeedleClientFactory(NeedleOptions options, INeedleArtifactProvider artifacts,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
    {
        _options = options;
        _artifacts = artifacts;
        _loggerFactory = loggerFactory;
    }

    public ValueTask<NeedleClient> CreateAsync(CancellationToken cancellationToken = default) =>
        NeedleClient.CreateAsync(_options, _artifacts, _loggerFactory, cancellationToken);
}
