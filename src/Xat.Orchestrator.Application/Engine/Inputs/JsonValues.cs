using System.Text.Json;

namespace Xat.Orchestrator.Application.Engine.Inputs;

/// <summary>Converts JSON values to plain .NET objects, for example for a template.</summary>
public static class JsonValues
{
    /// <summary>Converts a JSON element to a .NET object.</summary>
    /// <param name="element">The element.</param>
    /// <returns>
    /// <see langword="null"/>, a <see cref="bool"/>, a <see cref="long"/>, a <see cref="double"/>, a <see cref="string"/>,
    /// a list or a dictionary with text keys.
    /// </returns>
    public static object? ToObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    dictionary[property.Name] = ToObject(property.Value);
                }

                return dictionary;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(ToObject).ToList();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out var integer) ? integer : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    /// <summary>
    /// Compares two JSON values by their meaning, not by their text. Text is compared after the escapes are decoded,
    /// numbers by value (so <c>1</c> and <c>1.0</c> are equal), arrays in order and objects in any property order.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool AreEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => NumbersAreEqual(left, right),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength()
                && left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => AreEqual(pair.First, pair.Second)),
            JsonValueKind.Object => ObjectsAreEqual(left, right),
            _ => true,
        };
    }

    private static bool NumbersAreEqual(JsonElement left, JsonElement right) =>
        left.TryGetDecimal(out var a) && right.TryGetDecimal(out var b)
            ? a == b
            : left.GetDouble().Equals(right.GetDouble());

    private static bool ObjectsAreEqual(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToList();
        return leftProperties.Count == right.EnumerateObject().Count()
            && leftProperties.All(property => right.TryGetProperty(property.Name, out var other) && AreEqual(property.Value, other));
    }
}
