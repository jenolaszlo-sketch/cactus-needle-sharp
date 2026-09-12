using System.Text.Json;

namespace CactusNeedleSharp;

/// <summary>Provides helpers for deserializing tool-call arguments.</summary>
public static class NeedleToolCallExtensions
{
    /// <summary>Deserializes a call's arguments as <typeparamref name="TArguments"/>.</summary>
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
}
