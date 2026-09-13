using System.Text;

namespace CactusNeedleSharp;

internal static class NeedleValidation
{
    internal static void NativeText(string? value, string parameterName)
    {
        if (value is null) return;
        if (value.IndexOf('\0') >= 0)
            throw new ArgumentException("Text passed to the native Needle ABI cannot contain NUL characters.", parameterName);
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException exception)
        { throw new ArgumentException("Text contains invalid UTF-16 characters.", parameterName, exception); }
    }

    internal static void CompilationOptions(NeedleCompilationOptions? options)
    {
        if (options?.MaxNewTokens is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxNewTokens), "MaxNewTokens must be positive.");
    }

    internal static void SessionOptions(NeedleSessionOptions? session, NeedleOptions defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        NativeText(session?.SystemFacts ?? session?.Facts?.ToString(), nameof(session.SystemFacts));
        NativeText(session?.WeightsPath ?? defaults.ModelPath, nameof(session.WeightsPath));
        NativeText(session?.ToolIndexPath ?? defaults.ToolIndexPath, nameof(session.ToolIndexPath));
    }

    internal static NeedleTool[] Tools(IReadOnlyList<NeedleTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (tools.Count == 0) throw new NeedleSchemaException("At least one tool is required.");
        var snapshot = new NeedleTool[tools.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < tools.Count; index++)
        {
            var tool = tools[index] ?? throw new NeedleSchemaException($"Tool at index {index} is null.");
            tool.ValidateForUse();
            if (!names.Add(tool.Name)) throw new NeedleSchemaException($"Tool name '{tool.Name}' is duplicated.");
            // Preserve a typed tool's retained serializer contract while
            // detaching the JsonElement from caller-owned document storage.
            snapshot[index] = tool with { Parameters = tool.Parameters.Clone() };
        }
        return snapshot;
    }
}
