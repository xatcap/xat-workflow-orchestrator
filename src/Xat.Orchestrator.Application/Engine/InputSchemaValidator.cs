using System.Text.Json;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>
/// Checks the input of a caller against the input schema of a pipeline.
/// This validator supports a subset of JSON Schema. <see cref="CheckSchema(JsonElement)"/> rejects a keyword that it does not support,
/// so that a schema never has a rule that nothing enforces.
/// </summary>
/// <remarks>
/// Supported keywords: <c>type</c>, <c>properties</c>, <c>required</c>, <c>additionalProperties</c> (only <c>false</c>),
/// <c>items</c>, <c>enum</c>, <c>minLength</c>, <c>maxLength</c>, <c>minimum</c>, <c>maximum</c>, <c>minItems</c>, <c>maxItems</c>.
/// The keywords <c>title</c>, <c>description</c>, <c>default</c>, <c>examples</c>, <c>$schema</c> and <c>$id</c> are notes.
/// </remarks>
public static class InputSchemaValidator
{
    private static readonly HashSet<string> _supported = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties", "items", "enum",
        "minLength", "maxLength", "minimum", "maximum", "minItems", "maxItems",
        "title", "description", "default", "examples", "$schema", "$id",
    };

    /// <summary>Finds the keywords of a schema that this validator does not support.</summary>
    /// <param name="schema">The schema. An undefined value means that the pipeline accepts any input.</param>
    /// <returns>The messages. An empty list means that the validator supports the whole schema.</returns>
    public static IReadOnlyList<string> CheckSchema(JsonElement schema)
    {
        var messages = new List<string>();
        CheckSchema(schema, "input schema", messages);
        return messages;
    }

    /// <summary>Checks a value against a schema.</summary>
    /// <param name="schema">The schema. An undefined value means that any value is valid.</param>
    /// <param name="value">The value of the caller.</param>
    /// <returns>The messages. Each message names a path and a rule, and never repeats a value.</returns>
    public static IReadOnlyList<string> Validate(JsonElement schema, JsonElement value)
    {
        var messages = new List<string>();
        Visit(schema, value, "input", messages);
        return messages;
    }

    private static void CheckSchema(JsonElement schema, string path, List<string> messages)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in schema.EnumerateObject())
        {
            if (!_supported.Contains(property.Name))
            {
                messages.Add($"The {path} uses the keyword '{property.Name}', which is not supported.");
            }
        }

        if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind != JsonValueKind.False)
        {
            messages.Add($"The {path} sets additionalProperties to a value other than false, which is not supported.");
        }

        if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                CheckSchema(property.Value, $"{path}.{property.Name}", messages);
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            CheckSchema(items, $"{path}[]", messages);
        }
    }

    private static void Visit(JsonElement schema, JsonElement value, string path, List<string> messages)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("type", out var type) && !MatchesType(type, value))
        {
            messages.Add($"{path}: the type is not valid. Expected {DescribeType(type)}.");
            return;
        }

        if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array
            && !allowed.EnumerateArray().Any(item => item.GetRawText() == value.GetRawText()))
        {
            messages.Add($"{path}: the value is not one of the allowed values.");
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                CheckLength(schema, value.GetString()!.Length, path, "minLength", "maxLength", "characters", messages);
                break;
            case JsonValueKind.Number:
                CheckRange(schema, value.GetDouble(), path, messages);
                break;
            case JsonValueKind.Array:
                CheckLength(schema, value.GetArrayLength(), path, "minItems", "maxItems", "items", messages);
                if (schema.TryGetProperty("items", out var itemSchema))
                {
                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        Visit(itemSchema, item, $"{path}[{index}]", messages);
                        index++;
                    }
                }

                break;
            case JsonValueKind.Object:
                VisitObject(schema, value, path, messages);
                break;
        }
    }

    private static void VisitObject(JsonElement schema, JsonElement value, string path, List<string> messages)
    {
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray().Select(item => item.GetString()))
            {
                if (name is not null && !value.TryGetProperty(name, out _))
                {
                    messages.Add($"{path}.{name}: the property is required.");
                }
            }
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object;
        var closed = schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False;
        foreach (var member in value.EnumerateObject())
        {
            if (hasProperties && properties.TryGetProperty(member.Name, out var memberSchema))
            {
                Visit(memberSchema, member.Value, $"{path}.{member.Name}", messages);
            }
            else if (closed)
            {
                messages.Add($"{path}.{member.Name}: the property is not allowed.");
            }
        }
    }

    private static void CheckLength(
        JsonElement schema,
        int length,
        string path,
        string minKeyword,
        string maxKeyword,
        string unit,
        List<string> messages)
    {
        if (schema.TryGetProperty(minKeyword, out var min) && min.TryGetInt32(out var minimum) && length < minimum)
        {
            messages.Add($"{path}: the length is too small. The minimum is {minimum} {unit}.");
        }

        if (schema.TryGetProperty(maxKeyword, out var max) && max.TryGetInt32(out var maximum) && length > maximum)
        {
            messages.Add($"{path}: the length is too large. The maximum is {maximum} {unit}.");
        }
    }

    private static void CheckRange(JsonElement schema, double number, string path, List<string> messages)
    {
        if (schema.TryGetProperty("minimum", out var min) && min.TryGetDouble(out var minimum) && number < minimum)
        {
            messages.Add($"{path}: the number is too small. The minimum is {minimum}.");
        }

        if (schema.TryGetProperty("maximum", out var max) && max.TryGetDouble(out var maximum) && number > maximum)
        {
            messages.Add($"{path}: the number is too large. The maximum is {maximum}.");
        }
    }

    private static bool MatchesType(JsonElement type, JsonElement value)
    {
        if (type.ValueKind == JsonValueKind.Array)
        {
            return type.EnumerateArray().Any(item => MatchesType(item, value));
        }

        return type.GetString() switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && IsIntegral(value),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => false,
        };
    }

    private static bool IsIntegral(JsonElement number) =>
        number.TryGetInt64(out _) || (number.TryGetDouble(out var real) && Math.Abs(real % 1) < double.Epsilon);

    private static string DescribeType(JsonElement type) =>
        type.ValueKind == JsonValueKind.Array
            ? string.Join(" or ", type.EnumerateArray().Select(item => item.GetString()))
            : type.GetString() ?? "unknown";
}
