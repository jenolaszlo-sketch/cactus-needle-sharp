using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CactusNeedleSharp;

// Supports the managed schema generator and common tool schema assertions.
// Treat unsupported assertions as unverifiable so they cannot confer a successful outcome.
internal static class NeedleArgumentSchema
{
    internal static bool Matches(JsonElement value, JsonElement schema)
    {
        try { return MatchesCore(value, schema); }
        catch (Exception error) when (error is InvalidOperationException or FormatException or ArgumentException or RegexMatchTimeoutException)
        { return false; }
    }

    private static bool MatchesCore(JsonElement value, JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.True) return UniqueProperties(value);
        if (schema.ValueKind != JsonValueKind.Object || !UniqueProperties(schema) || !UniqueProperties(value)) return false;
        foreach (var rule in schema.EnumerateObject())
        {
            var valid = rule.Name switch
            {
                "$schema" or "$comment" or "title" or "description" or "default" or "examples" or
                    "deprecated" or "readOnly" or "writeOnly" => true,
                "type" => rule.Value.ValueKind == JsonValueKind.Array
                    ? rule.Value.EnumerateArray().Any(type => HasType(value, type.GetString()))
                    : HasType(value, rule.Value.GetString()),
                "enum" => rule.Value.EnumerateArray().Any(item => Equal(value, item)),
                "const" => Equal(value, rule.Value),
                "allOf" => rule.Value.EnumerateArray().All(item => MatchesCore(value, item)),
                "anyOf" => rule.Value.EnumerateArray().Any(item => MatchesCore(value, item)),
                "properties" or "additionalProperties" => true, // Evaluated together below.
                "required" => value.ValueKind != JsonValueKind.Object ||
                    rule.Value.EnumerateArray().All(item => value.TryGetProperty(item.GetString()!, out _)),
                "minProperties" => value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() >= rule.Value.GetInt32(),
                "maxProperties" => value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() <= rule.Value.GetInt32(),
                "items" => value.ValueKind != JsonValueKind.Array ||
                    value.EnumerateArray().All(item => MatchesCore(item, rule.Value)),
                "minItems" => value.ValueKind != JsonValueKind.Array || value.GetArrayLength() >= rule.Value.GetInt32(),
                "maxItems" => value.ValueKind != JsonValueKind.Array || value.GetArrayLength() <= rule.Value.GetInt32(),
                "minLength" => value.ValueKind != JsonValueKind.String || RuneCount(value) >= rule.Value.GetInt32(),
                "maxLength" => value.ValueKind != JsonValueKind.String || RuneCount(value) <= rule.Value.GetInt32(),
                "pattern" => value.ValueKind != JsonValueKind.String ||
                    Regex.IsMatch(value.GetString()!, rule.Value.GetString()!, RegexOptions.CultureInvariant | RegexOptions.ECMAScript, TimeSpan.FromMilliseconds(50)),
                "minimum" => value.ValueKind != JsonValueKind.Number || ExactDecimal(value) >= ExactDecimal(rule.Value),
                "maximum" => value.ValueKind != JsonValueKind.Number || ExactDecimal(value) <= ExactDecimal(rule.Value),
                "exclusiveMinimum" => value.ValueKind != JsonValueKind.Number || ExactDecimal(value) > ExactDecimal(rule.Value),
                "exclusiveMaximum" => value.ValueKind != JsonValueKind.Number || ExactDecimal(value) < ExactDecimal(rule.Value),
                "multipleOf" => value.ValueKind != JsonValueKind.Number ||
                    (ExactDecimal(rule.Value) > 0 && ExactDecimal(value) % ExactDecimal(rule.Value) == 0),
                _ => false
            };
            if (!valid) return false;
        }

        if (value.ValueKind != JsonValueKind.Object) return true;
        var hasProperties = schema.TryGetProperty("properties", out var properties);
        var hasAdditional = schema.TryGetProperty("additionalProperties", out var additional);
        if (hasProperties && properties.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
        {
            if (hasProperties && properties.TryGetProperty(property.Name, out var child))
            {
                if (!MatchesCore(property.Value, child)) return false;
            }
            else if (hasAdditional && !MatchesCore(property.Value, additional))
                return false;
        }
        return true;
    }

    private static decimal ExactDecimal(JsonElement element)
    {
        var number = element.GetDecimal();
        if (NormalizeNumber(element.GetRawText()) != NormalizeNumber(number.ToString(CultureInfo.InvariantCulture)))
            throw new FormatException("The schema comparison requires an exactly representable decimal.");
        return number;
    }

    private static (bool Negative, string Digits, long Exponent) NormalizeNumber(string text)
    {
        var exponentMarker = text.AsSpan().IndexOfAny('e', 'E');
        var significand = exponentMarker < 0 ? text : text[..exponentMarker];
        var digits = significand.TrimStart('-').Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return (false, "", 0);
        var point = significand.IndexOf('.');
        var places = point < 0 ? 0 : significand.Length - point - 1;
        var explicitExponent = exponentMarker < 0 ? 0 : int.Parse(text.AsSpan(exponentMarker + 1),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var significant = digits.TrimEnd('0');
        return (text[0] == '-', significant, (long)explicitExponent - places + digits.Length - significant.Length);
    }

    private static bool IsInteger(JsonElement value)
    {
        var number = ExactDecimal(value);
        return decimal.Truncate(number) == number;
    }

    private static int RuneCount(JsonElement value) => value.GetString()!.EnumerateRunes().Count();

    private static bool HasType(JsonElement value, string? type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "null" => value.ValueKind == JsonValueKind.Null,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && IsInteger(value),
        _ => false
    };

    internal static bool UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().All(UniqueProperties);
        if (value.ValueKind != JsonValueKind.Object) return true;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().All(property => names.Add(property.Name) && UniqueProperties(property.Value));
    }

    private static bool Equal(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.Object => left.EnumerateObject().Count() == right.EnumerateObject().Count() &&
                left.EnumerateObject().All(property => right.TryGetProperty(property.Name, out var value) && Equal(property.Value, value)),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength() &&
                left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => Equal(pair.First, pair.Second)),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => ExactDecimal(left) == ExactDecimal(right),
            _ => true
        };
    }
}
