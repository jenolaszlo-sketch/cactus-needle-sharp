using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CactusNeedleSharp;

internal static class JsonSchemaGenerator
{
    // Closed world for the trim-safe path: detecting arbitrary IEnumerable<T>
    // implementations needs interface enumeration, which trimming cannot see.
    // Dictionaries and exotic collections resolve through nested or explicit schema.
    private static readonly HashSet<Type> SequenceDefinitions =
    [
        typeof(IEnumerable<>),
        typeof(ICollection<>),
        typeof(IList<>),
        typeof(IReadOnlyCollection<>),
        typeof(IReadOnlyList<>),
        typeof(ISet<>),
        typeof(List<>),
        typeof(HashSet<>),
        typeof(SortedSet<>),
        typeof(Queue<>),
        typeof(Stack<>),
        typeof(LinkedList<>)
    ];

    [RequiresUnreferencedCode("Schema generation reflects over the supplied type. Use Generate(JsonTypeInfo) for trimmed hosts.")]
    [RequiresDynamicCode("Schema generation reflects over the supplied type. Use Generate(JsonTypeInfo) for NativeAOT hosts.")]
    public static JsonElement Generate(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.Interfaces)] Type type,
        JsonSerializerOptions? options)
    {
        options ??= new(JsonSerializerDefaults.Web);
        return Build(type, options, new HashSet<Type>()).Deserialize<JsonElement>();
    }

    /// <summary>
    /// Builds a schema from serializer metadata instead of reflection, so
    /// trimmed and NativeAOT hosts can generate schemas for the types in
    /// their own <see cref="JsonSerializerContext"/>. Nested object types
    /// resolve through <paramref name="nested"/>; when it returns null the
    /// schema names the missing type explicitly instead of guessing.
    /// Required follows serializer required-ness (required members and
    /// non-nullable value types); reference-type nullability annotations
    /// are a reflection-only signal, so mark such properties required
    /// explicitly when the schema must demand them.
    /// </summary>
    public static JsonElement Generate(JsonTypeInfo typeInfo, Func<Type, JsonTypeInfo?>? nested = null)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var node = typeInfo.Kind == JsonTypeInfoKind.Object
            ? BuildObject(typeInfo, nested, new HashSet<string>())
            : MapType(typeInfo.Type, nested, new HashSet<string>());
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonObject BuildObject(JsonTypeInfo typeInfo, Func<Type, JsonTypeInfo?>? nested, HashSet<string> stack)
    {
        var key = typeInfo.Type.FullName ?? typeInfo.Type.Name;
        if (!stack.Add(key)) throw new NeedleSchemaException($"Recursive type '{typeInfo.Type}' requires an explicit JSON Schema.");
        var policy = typeInfo.Options.PropertyNamingPolicy;
        var properties = new JsonObject();
        var requiredNames = new List<JsonNode?>();
        foreach (var property in typeInfo.Properties)
        {
            var name = policy?.ConvertName(property.Name) ?? property.Name;
            properties[name] = MapType(property.PropertyType, nested, stack);
            if (property.IsRequired) requiredNames.Add((JsonNode)name);
        }
        stack.Remove(key);
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(requiredNames.ToArray()),
            ["additionalProperties"] = false
        };
    }

    private static JsonNode MapType(Type type, Func<Type, JsonTypeInfo?>? nested, HashSet<string> stack)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return MapType(nullable, nested, stack);
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
            return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (type.IsEnum)
        {
            var names = new List<JsonNode?>();
            foreach (var name in Enum.GetNames(type)) names.Add((JsonNode)name);
            return new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(names.ToArray()) };
        }
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new JsonObject { ["type"] = "number" };
        if (type.IsPrimitive) return new JsonObject { ["type"] = "integer" };
        if (type.IsArray) return new JsonObject { ["type"] = "array", ["items"] = MapType(type.GetElementType()!, nested, stack) };
        if (type.IsGenericType && SequenceDefinitions.Contains(type.GetGenericTypeDefinition()))
            return new JsonObject { ["type"] = "array", ["items"] = MapType(type.GetGenericArguments()[0], nested, stack) };
        var resolved = nested?.Invoke(type);
        if (resolved is null)
            throw new NeedleSchemaException(
                $"Type '{type}' has no JSON metadata. Supply a nested resolver from your JsonSerializerContext or an explicit JSON Schema.");
        return resolved.Kind == JsonTypeInfoKind.Object
            ? BuildObject(resolved, nested, stack)
            : MapType(resolved.Type, nested, stack);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Recursion stays inside the reflection-based path already annotated RequiresUnreferencedCode.")]
    [UnconditionalSuppressMessage("Trimming", "IL2062", Justification = "Recursion stays inside the reflection-based path already annotated RequiresUnreferencedCode.")]
    private static JsonNode Build(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.Interfaces)] Type type,
        JsonSerializerOptions options,
        HashSet<Type> stack)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return Build(nullable, options, stack);
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
            return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (type.IsEnum)
        {
            var names = new List<JsonNode?>();
            foreach (var name in Enum.GetNames(type)) names.Add((JsonNode)name);
            return new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(names.ToArray()) };
        }
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return new JsonObject { ["type"] = "number" };
        if (type.IsPrimitive) return new JsonObject { ["type"] = "integer" };
        if (type.IsArray) return new JsonObject { ["type"] = "array", ["items"] = Build(type.GetElementType()!, options, stack) };
        var enumerable = type.GetInterfaces().Append(type).FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is not null && type != typeof(string))
            return new JsonObject { ["type"] = "array", ["items"] = Build(enumerable.GetGenericArguments()[0], options, stack) };
        if (!stack.Add(type)) throw new NeedleSchemaException($"Recursive type '{type}' requires an explicit JSON Schema.");
        var properties = new JsonObject();
        var requiredNames = new List<JsonNode?>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetMethod is not null))
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
            properties[name] = Build(property.PropertyType, options, stack);
            if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null ||
                new NullabilityInfoContext().Create(property).ReadState == NullabilityState.NotNull) requiredNames.Add((JsonNode)name);
        }
        stack.Remove(type);
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(requiredNames.ToArray()), ["additionalProperties"] = false };
    }
}


