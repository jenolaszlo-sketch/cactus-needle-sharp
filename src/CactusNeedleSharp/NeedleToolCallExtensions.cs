using System.Text.Json;

namespace CactusNeedleSharp;

public static class NeedleToolCallExtensions
{
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
