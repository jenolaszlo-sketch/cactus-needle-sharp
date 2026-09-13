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
    // Exotic collections resolve through nested metadata or an explicit schema.
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
        var node = MapMetadataType(typeInfo, nested, new HashSet<Type>());
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonObject BuildObject(JsonTypeInfo typeInfo, Func<Type, JsonTypeInfo?>? nested, HashSet<Type> stack)
    {
        if (!stack.Add(typeInfo.Type)) throw new NeedleSchemaException($"Recursive type '{typeInfo.Type}' requires an explicit JSON Schema.");
        var properties = new JsonObject();
        var requiredNames = new List<JsonNode?>();
        foreach (var property in typeInfo.Properties)
        {
            if (property.Get is null && property.Set is null) continue;
            // JsonPropertyInfo.Name is already the resolved wire name. Applying
            // the options policy again corrupts explicit and non-idempotent names.
            var name = property.Name;
            properties[name] = MapType(property.PropertyType, nested, stack, typeInfo.Options);
            if (property.IsRequired) requiredNames.Add((JsonNode)name);
        }
        stack.Remove(typeInfo.Type);
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(requiredNames.ToArray()),
            ["additionalProperties"] = false
        };
    }

    private static JsonNode MapType(Type type, Func<Type, JsonTypeInfo?>? nested, HashSet<Type> stack, JsonSerializerOptions? metadataOptions = null)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return MapType(nullable, nested, stack, metadataOptions);
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
            return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (type.IsEnum)
        {
            if (metadataOptions is not null)
            {
                try
                {
                    var enumInfo = metadataOptions.GetTypeInfo(type);
                    if (enumInfo is not null) return BuildMetadataEnum(enumInfo);
                }
                catch (NotSupportedException) { }
            }
            return BuildReflectionEnum(type, null);
        }
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new JsonObject { ["type"] = "number" };
        if (type.IsPrimitive) return new JsonObject { ["type"] = "integer" };
        if (type.IsArray) return new JsonObject { ["type"] = "array", ["items"] = MapType(type.GetElementType()!, nested, stack, metadataOptions) };
        if (type.IsGenericType && SequenceDefinitions.Contains(type.GetGenericTypeDefinition()))
            return new JsonObject { ["type"] = "array", ["items"] = MapType(type.GetGenericArguments()[0], nested, stack, metadataOptions) };
        if (TryGetDictionaryTypes(type, out var keyType, out var valueType))
        {
            if (keyType != typeof(string))
                throw new NeedleSchemaException($"Dictionary type '{type}' must use string keys for JSON object schema generation.");
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = MapType(valueType, nested, stack, metadataOptions) };
        }
        var resolved = nested?.Invoke(type);
        if (resolved is null)
            throw new NeedleSchemaException(
                $"Type '{type}' has no JSON metadata. Supply a nested resolver from your JsonSerializerContext or an explicit JSON Schema.");
        if (resolved.Type == type && resolved.Kind != JsonTypeInfoKind.Object)
            throw new NeedleSchemaException($"Metadata for '{type}' does not describe a progressable JSON shape.");
        return MapMetadataType(resolved, nested, stack);
    }

    private static JsonNode MapMetadataType(JsonTypeInfo info, Func<Type, JsonTypeInfo?>? nested, HashSet<Type> stack)
    {
        if (info.Kind == JsonTypeInfoKind.Object) return BuildObject(info, nested, stack);
        if (info.Kind == JsonTypeInfoKind.Dictionary)
        {
            if (!TryGetDictionaryTypes(info.Type, out var keyType, out var element) || keyType != typeof(string))
                throw new NeedleSchemaException($"Dictionary type '{info.Type}' must use string keys for JSON object schema generation.");
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = MapNestedOrPrimitive(element, nested, stack, info.Options) };
        }
        if (info.Kind == JsonTypeInfoKind.Enumerable)
        {
            var element = info.Type.IsArray ? info.Type.GetElementType() : info.Type.GetGenericArguments().FirstOrDefault();
            if (element is null) throw new NeedleSchemaException($"Enumerable type '{info.Type}' has no element metadata.");
            return new JsonObject { ["type"] = "array", ["items"] = MapNestedOrPrimitive(element, nested, stack, info.Options) };
        }
        if (info.Type.IsEnum) return BuildMetadataEnum(info);
        return MapType(info.Type, nested, stack, info.Options);
    }

    private static JsonNode MapNestedOrPrimitive(Type type, Func<Type, JsonTypeInfo?>? nested, HashSet<Type> stack,
        JsonSerializerOptions options)
    {
        var resolved = nested?.Invoke(type);
        return resolved is null ? MapType(type, nested, stack, options) : MapMetadataType(resolved, nested, stack);
    }

    private static JsonObject BuildMetadataEnum(JsonTypeInfo info)
    {
        var values = Enum.GetValuesAsUnderlyingType(info.Type);
        var serialized = values.Cast<object>().Select(value => JsonSerializer.SerializeToElement(Enum.ToObject(info.Type, value), info)).ToArray();
        var kind = serialized.All(value => value.ValueKind == JsonValueKind.String) ? "string" : "integer";
        var entries = serialized.Select(value => JsonNode.Parse(value.GetRawText())!).ToArray();
        return new JsonObject { ["type"] = kind, ["enum"] = new JsonArray(entries) };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This helper is only used by the reflection schema path, which is already annotated.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This helper is only used by the reflection schema path, which is already annotated.")]
    private static JsonObject BuildReflectionEnum(Type type, JsonSerializerOptions? options)
    {
        var values = Enum.GetValuesAsUnderlyingType(type).Cast<object>().ToArray();
        var serialized = values.Select(value => JsonSerializer.SerializeToElement(Enum.ToObject(type, value), type, options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web))).ToArray();
        var kind = serialized.All(value => value.ValueKind == JsonValueKind.String) ? "string" : "integer";
        var entries = serialized.Select(value => JsonNode.Parse(value.GetRawText())!).ToArray();
        return new JsonObject { ["type"] = kind, ["enum"] = new JsonArray(entries) };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Caller is the reflection schema path or metadata type is already rooted.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection schema generation is already annotated.")]
    private static bool TryGetDictionaryTypes(Type type, out Type keyType, out Type valueType)
    {
        var dictionary = type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
            candidate.IsGenericType &&
            (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
             candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        if (dictionary is not null)
        {
            var arguments = dictionary.GetGenericArguments();
            keyType = arguments[0];
            valueType = arguments[1];
            return true;
        }
        keyType = null!;
        valueType = null!;
        return false;
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
            return BuildReflectionEnum(type, options);
        }
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return new JsonObject { ["type"] = "number" };
        if (type.IsPrimitive) return new JsonObject { ["type"] = "integer" };
        if (type.IsArray) return new JsonObject { ["type"] = "array", ["items"] = Build(type.GetElementType()!, options, stack) };
        if (TryGetDictionaryTypes(type, out var dictionaryKey, out var dictionaryValue))
        {
            if (dictionaryKey != typeof(string))
                throw new NeedleSchemaException($"Dictionary type '{type}' must use string keys for JSON object schema generation.");
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = Build(dictionaryValue, options, stack) };
        }
        var enumerable = type.GetInterfaces().Append(type).FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is not null && type != typeof(string))
            return new JsonObject { ["type"] = "array", ["items"] = Build(enumerable.GetGenericArguments()[0], options, stack) };
        if (!stack.Add(type)) throw new NeedleSchemaException($"Recursive type '{type}' requires an explicit JSON Schema.");
        var properties = new JsonObject();
        var requiredNames = new List<JsonNode?>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetMethod is not null))
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: not JsonIgnoreCondition.Never }) continue;
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
            properties[name] = Build(property.PropertyType, options, stack);
            if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null ||
                new NullabilityInfoContext().Create(property).ReadState == NullabilityState.NotNull) requiredNames.Add((JsonNode)name);
        }
        stack.Remove(type);
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(requiredNames.ToArray()), ["additionalProperties"] = false };
    }
}


