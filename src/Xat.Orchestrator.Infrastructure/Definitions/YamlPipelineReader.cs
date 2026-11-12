using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Xat.Orchestrator.Infrastructure.Definitions;

/// <summary>
/// Reads a pipeline file. The reader turns the YAML into JSON first, because the options of a step and the input schema
/// are JSON values. The reader rejects unknown keys, so a spelling mistake never goes unseen.
/// </summary>
internal static class YamlPipelineReader
{
    private static readonly HashSet<string> _topKeys = new(StringComparer.Ordinal)
    {
        "name", "version", "summary", "stream", "input", "steps",
    };

    private static readonly HashSet<string> _stepKeys = new(StringComparer.Ordinal) { "id", "uses", "with" };

    /// <summary>Reads one pipeline file.</summary>
    /// <param name="yaml">The text of the file.</param>
    /// <param name="source">The name of the file, for error messages.</param>
    /// <returns>The pipeline, or a validation error that lists all problems.</returns>
    public static Result<PipelineDefinition> Read(string yaml, string source)
    {
        var problems = new List<string>();
        JsonNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents.Count == 0 ? null : ToJson(stream.Documents[0].RootNode);
        }
        catch (YamlException exception)
        {
            return Fail($"{source}: the YAML is not valid. {exception.Message}");
        }

        if (root is not JsonObject top)
        {
            return Fail($"{source}: the file must hold a YAML mapping.");
        }

        foreach (var key in top.Select(pair => pair.Key).Where(key => !_topKeys.Contains(key)))
        {
            problems.Add($"{source}: the key '{key}' is not known. Known keys: {string.Join(", ", _topKeys.Order(StringComparer.Ordinal))}.");
        }

        foreach (var key in new[] { "name", "summary", "stream" })
        {
            if (top[key] is not null && Text(top, key) is null)
            {
                problems.Add($"{source}: the key '{key}' must be text. Put a number in quotes if you want text.");
            }
        }

        var name = Text(top, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add($"{source}: the key 'name' is required.");
        }

        var version = ReadVersion(top["version"]);
        if (version is null)
        {
            problems.Add($"{source}: the key 'version' must be an integer of 1 or higher.");
        }

        var streamMode = ReadStreamMode(Text(top, "stream"));
        if (streamMode is null)
        {
            problems.Add($"{source}: the key 'stream' must be allowed, never or required.");
        }

        var schema = top["input"] is JsonObject inputSchema
            ? JsonSerializer.SerializeToElement(inputSchema)
            : JsonSerializer.SerializeToElement(new JsonObject());
        if (top["input"] is not null and not JsonObject)
        {
            problems.Add($"{source}: the key 'input' must be a mapping that holds a JSON Schema.");
        }

        var steps = ReadSteps(top, source, problems);
        if (problems.Count > 0)
        {
            return Fail(string.Join(" ", problems));
        }

        return Result<PipelineDefinition>.Ok(
            new PipelineDefinition(name!, version!.Value, Text(top, "summary") ?? string.Empty, streamMode!.Value, schema, steps));
    }

    /// <summary>Reads the version. The reader stores a whole number as a long, so it reads a long and checks the range.</summary>
    /// <returns>The version, 1 when the key is not set, or <see langword="null"/> when the value is not valid.</returns>
    private static int? ReadVersion(JsonNode? node)
    {
        if (node is null)
        {
            return 1;
        }

        return node is JsonValue number && number.TryGetValue<long>(out var parsed) && parsed is >= 1 and <= int.MaxValue
            ? (int)parsed
            : null;
    }

    /// <summary>Reads the stream mode by name only. A number or a list of flags, such as <c>7</c> or <c>1,2</c>, is not valid.</summary>
    /// <returns>The mode, <see cref="StreamMode.Allowed"/> when the key is not set, or <see langword="null"/> when the value is not valid.</returns>
    private static StreamMode? ReadStreamMode(string? text) =>
        text?.ToLowerInvariant() switch
        {
            null or "allowed" => StreamMode.Allowed,
            "never" => StreamMode.Never,
            "required" => StreamMode.Required,
            _ => null,
        };

    private static List<StepDefinition> ReadSteps(JsonObject top, string source, List<string> problems)
    {
        var steps = new List<StepDefinition>();
        if (top["steps"] is not JsonArray array)
        {
            problems.Add($"{source}: the key 'steps' must be a list.");
            return steps;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject step)
            {
                problems.Add($"{source}: step {i + 1} must be a mapping.");
                continue;
            }

            foreach (var key in step.Select(pair => pair.Key).Where(key => !_stepKeys.Contains(key)))
            {
                problems.Add($"{source}: step {i + 1} has the key '{key}', which is not known. Known keys: id, uses, with.");
            }

            foreach (var key in new[] { "id", "uses" })
            {
                if (step[key] is not null && Text(step, key) is null)
                {
                    problems.Add($"{source}: the key '{key}' of step {i + 1} must be text. Put a number in quotes if you want text.");
                }
            }

            var uses = Text(step, "uses");
            if (string.IsNullOrWhiteSpace(uses))
            {
                problems.Add($"{source}: step {i + 1} needs the key 'uses'.");
                continue;
            }

            if (step["with"] is not null and not JsonObject)
            {
                problems.Add($"{source}: the key 'with' of step {i + 1} must be a mapping.");
                continue;
            }

            var id = Text(step, "id") ?? DefaultId(uses, used);
            used.Add(id);
            var with = JsonSerializer.SerializeToElement(step["with"] ?? new JsonObject());
            steps.Add(new StepDefinition(id, uses, with));
        }

        return steps;
    }

    /// <summary>Makes the ID of a step that has no <c>id</c> key: the step type with dots as hyphens, and a number for a repeat.</summary>
    private static string DefaultId(string uses, HashSet<string> used)
    {
        var baseId = uses.Replace('.', '-');
        if (!used.Contains(baseId))
        {
            return baseId;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{baseId}-{n}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string? Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonNode? ToJson(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                var obj = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    var name = key is YamlScalarNode scalarKey
                        ? scalarKey.Value ?? string.Empty
                        : throw new YamlException(key.Start, key.End, "A key must be text, not a list or a mapping.");
                    obj[name] = ToJson(value);
                }

                return obj;
            case YamlSequenceNode sequence:
                var array = new JsonArray();
                foreach (var item in sequence.Children)
                {
                    array.Add(ToJson(item));
                }

                return array;
            case YamlScalarNode scalar:
                return ScalarToJson(scalar);
            default:
                return null;
        }
    }

    private static JsonValue? ScalarToJson(YamlScalarNode scalar)
    {
        var text = scalar.Value;

        // A quoted value is always text. Only a plain value can be a number, a flag or null.
        if (scalar.Style != ScalarStyle.Plain || text is null)
        {
            return text is null ? null : JsonValue.Create(text);
        }

        if (text is "" or "~" or "null")
        {
            return null;
        }

        if (text is "true" or "false")
        {
            return JsonValue.Create(text == "true");
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) && !double.IsNaN(real) && !double.IsInfinity(real))
        {
            return JsonValue.Create(real);
        }

        return JsonValue.Create(text);
    }

    private static Result<PipelineDefinition> Fail(string message) =>
        Result<PipelineDefinition>.Fail(new PipelineError("pipeline.file_invalid", message, ErrorKind.Validation));
}
