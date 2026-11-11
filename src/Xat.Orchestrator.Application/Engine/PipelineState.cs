using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>The data of one run. Steps write to the state and read from it. One run has one state, and runs never share it.</summary>
public sealed class PipelineState
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, object?>> _outputs = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];

    /// <summary>Initializes a new instance of the <see cref="PipelineState"/> class.</summary>
    /// <param name="input">The input that the caller sent, as a JSON object.</param>
    public PipelineState(JsonElement input)
    {
        Input = input;
    }

    /// <summary>Gets the input that the caller sent.</summary>
    public JsonElement Input { get; }

    /// <summary>Gets the prompt that the steps build.</summary>
    public PromptDraft Prompt { get; } = new();

    /// <summary>Gets the notes about the run. A note is not an error.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Gets the sum of the token usage of all LLM calls of the run.</summary>
    public TokenUsage Usage { get; private set; } = TokenUsage.None;

    /// <summary>Adds a note about the run.</summary>
    /// <param name="warning">The note. It must not repeat an input value.</param>
    public void AddWarning(string warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);
        _warnings.Add(warning);
    }

    /// <summary>Adds the usage of one LLM call.</summary>
    /// <param name="usage">The usage.</param>
    public void AddUsage(TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        Usage = Usage.Add(usage);
    }

    /// <summary>Stores a value.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Set<T>(StateKey<T> key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _values[key.Name] = value;
    }

    /// <summary>Reads a value.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, when the key exists.</param>
    /// <returns><see langword="true"/> when the state holds a value of this type for the key.</returns>
    public bool TryGet<T>(StateKey<T> key, [MaybeNullWhen(false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_values.TryGetValue(key.Name, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Stores the named results of a step. Later steps and expressions read them as <c>steps.&lt;id&gt;.&lt;name&gt;</c>.</summary>
    /// <param name="stepId">The ID of the step.</param>
    /// <param name="output">The results of the step.</param>
    public void SetOutput(string stepId, IReadOnlyDictionary<string, object?> output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        ArgumentNullException.ThrowIfNull(output);
        _outputs[stepId] = output;
    }

    /// <summary>Reads the named results of a step.</summary>
    /// <param name="stepId">The ID of the step.</param>
    /// <param name="output">The results, when the step stored them.</param>
    /// <returns><see langword="true"/> when the step stored results.</returns>
    public bool TryGetOutput(string stepId, [MaybeNullWhen(false)] out IReadOnlyDictionary<string, object?> output) =>
        _outputs.TryGetValue(stepId, out output);

    /// <summary>
    /// Reads a value by its path. A path starts with <c>input.</c> and then names JSON properties or array positions,
    /// or it has the form <c>steps.&lt;id&gt;.&lt;name&gt;</c>.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="value">The value, when the path exists. A JSON value is a <see cref="JsonElement"/>.</param>
    /// <returns><see langword="true"/> when the path exists.</returns>
    public bool TryLookup(string path, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (parts[0] == "input")
        {
            return TryLookupJson(Input, parts, out value);
        }

        if (parts[0] == "steps"
            && parts.Length == 3
            && _outputs.TryGetValue(parts[1], out var output)
            && output.TryGetValue(parts[2], out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryLookupJson(JsonElement root, string[] parts, out object? value)
    {
        var current = root;
        for (var i = 1; i < parts.Length; i++)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(parts[i], out var next))
            {
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array
                     && int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                     && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                value = null;
                return false;
            }
        }

        value = current;
        return true;
    }
}
