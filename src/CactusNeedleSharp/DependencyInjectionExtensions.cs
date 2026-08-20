using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CactusNeedleSharp;

public static class DependencyInjectionExtensions
{
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
