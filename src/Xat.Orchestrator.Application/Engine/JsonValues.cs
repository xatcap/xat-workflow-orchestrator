using System.Text.Json;

namespace Xat.Orchestrator.Application.Engine;

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
}
