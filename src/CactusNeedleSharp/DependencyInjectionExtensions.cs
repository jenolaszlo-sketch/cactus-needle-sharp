using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CactusNeedleSharp;

/// <summary>Registers Needle client and worker-pool services.</summary>
public static class DependencyInjectionExtensions
{
    /// <summary>Registers Needle options, artifacts, model manager, and client factory as singletons.</summary>
    public static IServiceCollection AddCactusNeedleSharp(this IServiceCollection services, NeedleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        options ??= new();
        services.TryAddSingleton(options);
        services.TryAddSingleton<INeedleArtifactProvider>(provider =>
            new HuggingFaceNeedleArtifactProvider(provider.GetRequiredService<NeedleOptions>()));
        services.TryAddSingleton<NeedleModelManager>();
        services.TryAddSingleton<INeedleClientFactory, NeedleClientFactory>();
        return services;
    }

    /// <summary>Registers a singleton <see cref="INeedleWorkerPool"/> built from <paramref name="options"/>.</summary>
    public static IServiceCollection AddCactusNeedleSharpWorkerPool(this IServiceCollection services,
        NeedleWorkerPoolOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<INeedleWorkerPool>(provider => new NeedleWorkerPool(
            provider.GetRequiredService<NeedleWorkerPoolOptions>(),
            provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()));
        return services;
    }
}
