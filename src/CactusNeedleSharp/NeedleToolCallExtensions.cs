using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CactusNeedleSharp;

/// <summary>Provides helpers for deserializing tool-call arguments.</summary>
public static class NeedleToolCallExtensions
{
    /// <summary>Deserializes a call's arguments as <typeparamref name="TArguments"/>.</summary>
    [RequiresUnreferencedCode("Argument deserialization reflects over the argument type. Use the JsonTypeInfo overload for trimmed hosts.")]
    [RequiresDynamicCode("Argument deserialization reflects over the argument type. Use the JsonTypeInfo overload for NativeAOT hosts.")]
    public static TArguments DeserializeArguments<TArguments>(this NeedleToolCall call,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            return call.Arguments.Deserialize<TArguments>(serializerOptions ?? NeedleProtocol.Json)
                ?? throw new NeedleProtocolException($"Tool '{call.Name}' arguments produced a null {typeof(TArguments).Name} value.");
        }
        catch (JsonException exception)
        {
            throw new NeedleProtocolException($"Tool '{call.Name}' arguments are invalid for {typeof(TArguments).Name}.", exception);
        }
    }

    /// <summary>Tries to deserialize a call's arguments, returning false with an error instead of throwing.</summary>
    [RequiresUnreferencedCode("Argument deserialization reflects over the argument type. Use the JsonTypeInfo overload for trimmed hosts.")]
    [RequiresDynamicCode("Argument deserialization reflects over the argument type. Use the JsonTypeInfo overload for NativeAOT hosts.")]
    public static bool TryDeserializeArguments<TArguments>(this NeedleToolCall call,
        out TArguments? arguments, out string? error,
        JsonSerializerOptions? serializerOptions = null)
    {
        try
        {
            arguments = call.DeserializeArguments<TArguments>(serializerOptions);
            error = null;
            return true;
        }
        catch (NeedleProtocolException exception)
        {
            arguments = default;
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Deserializes call arguments from serializer metadata instead of reflection.</summary>
    public static TArguments DeserializeArguments<TArguments>(this NeedleToolCall call,
        JsonTypeInfo<TArguments> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(typeInfo);
        try
        {
            return call.Arguments.Deserialize(typeInfo)
                ?? throw new NeedleProtocolException($"Tool '{call.Name}' arguments produced a null {typeof(TArguments).Name} value.");
        }
        catch (JsonException exception)
        {
            throw new NeedleProtocolException($"Tool '{call.Name}' arguments are invalid for {typeof(TArguments).Name}.", exception);
        }
    }

    /// <summary>Tries deserializing call arguments from serializer metadata instead of reflection.</summary>
    public static bool TryDeserializeArguments<TArguments>(this NeedleToolCall call,
        out TArguments? arguments, out string? error,
        JsonTypeInfo<TArguments> typeInfo)
    {
        try
        {
            arguments = call.DeserializeArguments(typeInfo);
            error = null;
            return true;
        }
        catch (NeedleProtocolException exception)
        {
            arguments = default;
            error = exception.Message;
            return false;
        }
    }
}
