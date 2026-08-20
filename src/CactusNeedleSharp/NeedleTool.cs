using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace CactusNeedleSharp;

public record NeedleTool
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required JsonElement Parameters { get; init; }

    public static NeedleTool Create(string name, string? description, JsonElement schema)
    {
        Validate(name, schema);
        return new() { Name = name, Description = description, Parameters = schema.Clone() };
    }

    public static NeedleTool Create(string name, string? description, JsonDocument schema) =>
        Create(name, description, schema.RootElement);

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

    public static NeedleTool<TArguments> FromType<TArguments>(string name, string? description = null,
        JsonSerializerOptions? serializerOptions = null) =>
        new(name, description, JsonSchemaGenerator.Generate(typeof(TArguments), serializerOptions));

    private static void Validate(string name, JsonElement schema)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new NeedleSchemaException("Tool name cannot be empty.");
        if (schema.ValueKind != JsonValueKind.Object) throw new NeedleSchemaException("Tool parameters must be a JSON object schema.");
    }
}

public sealed record NeedleTool<TArguments> : NeedleTool
{
    [SetsRequiredMembers]
    internal NeedleTool(string name, string? description, JsonElement parameters)
    { Name = name; Description = description; Parameters = parameters.Clone(); }
}
