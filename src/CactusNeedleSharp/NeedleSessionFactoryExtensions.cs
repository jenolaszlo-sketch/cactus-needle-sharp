using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CactusNeedleSharp;

/// <summary>Provides one-shot compilation and extraction over any Needle session factory.</summary>
public static class NeedleSessionFactoryExtensions
{
    /// <summary>Creates a short-lived session and compiles one input.</summary>
    public static async ValueTask<ToolCallCompilation> CompileAsync(this INeedleSessionFactory factory,
        string input, IReadOnlyList<NeedleTool> tools, NeedleCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        NeedleValidation.NativeText(input, nameof(input));
        NeedleValidation.CompilationOptions(options);
        await using var session = await factory.CreateSessionAsync(tools, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await session.CompleteAsync(input, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a short-lived session and extracts one typed value.</summary>
    [RequiresUnreferencedCode("Extraction reflects over the result type. Use the JsonTypeInfo overload for trimmed hosts.")]
    [RequiresDynamicCode("Extraction reflects over the result type. Use the JsonTypeInfo overload for NativeAOT hosts.")]
    public static async ValueTask<NeedleExtractionResult<T>> ExtractAsync<T>(this INeedleSessionFactory factory,
        string input, NeedleExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var tool = NeedleTool.FromType<T>("extract", options?.Description ?? $"Extract a {typeof(T).Name} record from text");
        var compilation = await factory.CompileAsync(input, [tool], new() { MaxNewTokens = options?.MaxNewTokens }, cancellationToken).ConfigureAwait(false);
        return NeedleExtractionResults.Create(compilation, "extract",
            arguments => arguments.Deserialize<T>(NeedleProtocol.Json), typeof(T).Name);
    }

    /// <summary>Creates a short-lived session and extracts one value using serializer metadata.</summary>
    public static async ValueTask<NeedleExtractionResult<T>> ExtractAsync<T>(this INeedleSessionFactory factory,
        string input, JsonTypeInfo<T> typeInfo, NeedleExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(typeInfo);
        var tool = NeedleTool.FromType("extract", typeInfo,
            options?.Description ?? $"Extract a {typeof(T).Name} record from text", options?.NestedTypeResolver);
        var compilation = await factory.CompileAsync(input, [tool], new() { MaxNewTokens = options?.MaxNewTokens }, cancellationToken).ConfigureAwait(false);
        return NeedleExtractionResults.Create(compilation, "extract", arguments => arguments.Deserialize(typeInfo), typeof(T).Name);
    }
}
