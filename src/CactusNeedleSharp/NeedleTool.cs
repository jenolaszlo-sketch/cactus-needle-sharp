using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace CactusNeedleSharp;

/// <summary>Describes a tool the model may call, with its JSON parameter schema.</summary>
public record NeedleTool
{
    /// <summary>Gets the tool name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the tool description, if any.</summary>
    public string? Description { get; init; }
    /// <summary>Gets the JSON object schema for the tool parameters.</summary>
    public required JsonElement Parameters { get; init; }

    /// <summary>Creates a tool from a JSON schema element.</summary>
    public static NeedleTool Create(string name, string? description, JsonElement schema)
    {
        Validate(name, schema);
        return new() { Name = name, Description = description, Parameters = schema.Clone() };
    }

    /// <summary>Creates a tool from a JSON schema document.</summary>
    public static NeedleTool Create(string name, string? description, JsonDocument schema) =>
        Create(name, description, schema.RootElement);

    /// <summary>Parses a tool from its JSON representation with name, description, and parameters.</summary>
    public static NeedleTool FromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return Create(root.GetProperty("name").GetString()!,
                root.TryGetProperty("description", out var description) ? description.GetString() : null,
                root.GetProperty("parameters"));
        }
        catch (NeedleSchemaException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new NeedleSchemaException("The tool definition is not valid Needle tool JSON.", exception); }
    }

    /// <summary>Generates a tool whose parameter schema is derived from <typeparamref name="TArguments"/>.</summary>
    public static NeedleTool<TArguments> FromType<TArguments>(string name, string? description = null,
        JsonSerializerOptions? serializerOptions = null) =>
        new(name, description, JsonSchemaGenerator.Generate(typeof(TArguments), serializerOptions));

    private static void Validate(string name, JsonElement schema)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new NeedleSchemaException("Tool name cannot be empty.");
        if (schema.ValueKind != JsonValueKind.Object) throw new NeedleSchemaException("Tool parameters must be a JSON object schema.");
    }
}

/// <summary>Describes a tool with a strongly-typed argument contract.</summary>
/// <typeparam name="TArguments">The argument type the schema was generated from.</typeparam>
public sealed record NeedleTool<TArguments> : NeedleTool
{
    [SetsRequiredMembers]
    internal NeedleTool(string name, string? description, JsonElement parameters)
    { Name = name; Description = description; Parameters = parameters.Clone(); }

    /// <summary>Deserializes a call's arguments as this tool's argument type.</summary>
    public TArguments DeserializeCallArguments(NeedleToolCall call, JsonSerializerOptions? serializerOptions = null) =>
        call.DeserializeArguments<TArguments>(serializerOptions);
}
