using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Engine.Inputs;

/// <summary>
/// Replaces the expressions in the <c>with</c> block of a step with values from the run state.
/// An expression is <c>${path}</c> or <c>${path ?? 'default'}</c>. This is a plain lookup, not a scripting language.
/// </summary>
public static partial class ExpressionResolver
{
    [GeneratedRegex(@"\$\{([^}]*)\}")]
    private static partial Regex ExpressionPattern();

    /// <summary>Resolves all expressions of a JSON value.</summary>
    /// <param name="with">The <c>with</c> block. An undefined value means an empty object.</param>
    /// <param name="state">The state of the run.</param>
    /// <returns>The JSON value with the expressions replaced, or an error.</returns>
    public static Result<JsonElement> Resolve(JsonElement with, PipelineState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (with.ValueKind == JsonValueKind.Undefined)
        {
            return Result<JsonElement>.Ok(JsonSerializer.SerializeToElement(new Dictionary<string, object?>()));
        }

        var context = new ResolveContext();
        var node = Walk(JsonNode.Parse(with.GetRawText()), state, context);
        return context.Error is null
            ? Result<JsonElement>.Ok(JsonSerializer.SerializeToElement(node))
            : Result<JsonElement>.Fail(new PipelineError("expression.invalid", context.Error, ErrorKind.Validation));
    }

    /// <summary>Finds the paths that the expressions of a JSON value use. The definition validator checks them.</summary>
    /// <param name="with">The <c>with</c> block.</param>
    /// <returns>The paths, without the default values.</returns>
    public static IReadOnlyList<string> FindPaths(JsonElement with)
    {
        var paths = new List<string>();
        Collect(with, paths);
        return paths;
    }

    private static void Collect(JsonElement element, List<string> paths)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                foreach (Match match in ExpressionPattern().Matches(element.GetString() ?? string.Empty))
                {
                    paths.Add(SplitDefault(match.Groups[1].Value).Path);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, paths);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, paths);
                }

                break;
        }
    }

    private static JsonNode? Walk(JsonNode? node, PipelineState state, ResolveContext context)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (name, value) in obj)
                {
                    result[name] = Walk(value, state, context);
                }

                return result;
            case JsonArray array:
                var list = new JsonArray();
                foreach (var item in array)
                {
                    list.Add(Walk(item, state, context));
                }

                return list;
            case JsonValue value when value.TryGetValue<string>(out var text):
                return ResolveString(text, state, context);
            default:
                return node?.DeepClone();
        }
    }

    private static JsonNode? ResolveString(string text, PipelineState state, ResolveContext context)
    {
        var matches = ExpressionPattern().Matches(text);
        if (matches.Count == 0)
        {
            return JsonValue.Create(text);
        }

        // A text that is one expression keeps the type of the value, for example a number or an object.
        if (matches.Count == 1 && matches[0].Length == text.Length)
        {
            return Evaluate(matches[0].Groups[1].Value, state, context);
        }

        var builder = new StringBuilder();
        var last = 0;
        foreach (Match match in matches)
        {
            builder.Append(text, last, match.Index - last);
            builder.Append(Stringify(Evaluate(match.Groups[1].Value, state, context)));
            last = match.Index + match.Length;
        }

        builder.Append(text, last, text.Length - last);
        return JsonValue.Create(builder.ToString());
    }

    private static JsonNode? Evaluate(string expression, PipelineState state, ResolveContext context)
    {
        var (path, fallback) = SplitDefault(expression);
        if (state.TryLookup(path, out var value) && value is not null)
        {
            var node = ToNode(value, path, context);
            if (context.Error is not null)
            {
                return null;
            }

            if (node is not null)
            {
                return node;
            }
        }

        return fallback is null ? null : JsonValue.Create(fallback);
    }

    private static JsonNode? ToNode(object value, string path, ResolveContext context)
    {
        switch (value)
        {
            case JsonElement element:
                return element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    ? null
                    : JsonNode.Parse(element.GetRawText());
            case string text:
                return JsonValue.Create(text);
            case bool flag:
                return JsonValue.Create(flag);
            case int number:
                return JsonValue.Create(number);
            case long number:
                return JsonValue.Create(number);
            case double number:
                return JsonValue.Create(number);
            default:
                context.Error = $"The value of '{path}' cannot be used in an expression.";
                return null;
        }
    }

    private static string Stringify(JsonNode? node) =>
        node switch
        {
            null => string.Empty,
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => node.ToJsonString(),
        };

    private static (string Path, string? Fallback) SplitDefault(string expression)
    {
        var index = expression.IndexOf("??", StringComparison.Ordinal);
        if (index < 0)
        {
            return (expression.Trim(), null);
        }

        var path = expression[..index].Trim();
        var literal = expression[(index + 2)..].Trim();
        if (literal.Length >= 2 && literal[0] is '\'' or '"' && literal[^1] == literal[0])
        {
            literal = literal[1..^1];
        }

        return (path, literal);
    }

    private sealed class ResolveContext
    {
        public string? Error { get; set; }
    }
}
