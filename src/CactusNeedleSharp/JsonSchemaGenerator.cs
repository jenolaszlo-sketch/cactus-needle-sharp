using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CactusNeedleSharp;

internal static class JsonSchemaGenerator
{
    public static JsonElement Generate(Type type, JsonSerializerOptions? options)
    {
        options ??= new(JsonSerializerDefaults.Web);
        return Build(type, options, new HashSet<Type>()).Deserialize<JsonElement>();
    }

    private static JsonNode Build(Type type, JsonSerializerOptions options, HashSet<Type> stack)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return Build(nullable, options, stack);
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
            return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (type.IsEnum) return new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Enum.GetNames(type).Select(name => JsonValue.Create(name)).ToArray()) };
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return new JsonObject { ["type"] = "number" };
        if (type.IsPrimitive) return new JsonObject { ["type"] = "integer" };
        if (type.IsArray) return new JsonObject { ["type"] = "array", ["items"] = Build(type.GetElementType()!, options, stack) };
        var enumerable = type.GetInterfaces().Append(type).FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is not null && type != typeof(string))
            return new JsonObject { ["type"] = "array", ["items"] = Build(enumerable.GetGenericArguments()[0], options, stack) };
        if (!stack.Add(type)) throw new NeedleSchemaException($"Recursive type '{type}' requires an explicit JSON Schema.");
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetMethod is not null))
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
            properties[name] = Build(property.PropertyType, options, stack);
            if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null ||
                new NullabilityInfoContext().Create(property).ReadState == NullabilityState.NotNull) required.Add(name);
        }
        stack.Remove(type);
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false };
    }
}
