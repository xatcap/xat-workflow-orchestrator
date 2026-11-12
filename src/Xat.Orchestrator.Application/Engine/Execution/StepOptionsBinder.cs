using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xat.Orchestrator.Application.Engine.Inputs;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Engine.Execution;

/// <summary>Turns the <c>with</c> block of a step into an options object and checks it.</summary>
internal static class StepOptionsBinder
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>Binds and validates the options of a step when the run starts. All expressions are already resolved.</summary>
    /// <typeparam name="TOptions">The class of the options.</typeparam>
    /// <param name="with">The <c>with</c> block as JSON. A missing block means an empty object.</param>
    /// <returns>The options, or a validation error.</returns>
    public static Result<TOptions> Bind<TOptions>(JsonElement with)
        where TOptions : class =>
        BindCore<TOptions>(Normalize(with), new HashSet<string>());

    /// <summary>
    /// Binds and validates the options of a step when the service starts. An option that is not text, such as a number or a flag,
    /// can hold one expression, for example <c>chunks: "${input.n}"</c>. Its value is known only when the run starts, so the method
    /// leaves out such an option and its rules. The run binds and checks it again with the real value.
    /// </summary>
    /// <typeparam name="TOptions">The class of the options.</typeparam>
    /// <param name="with">The <c>with</c> block as JSON. A missing block means an empty object.</param>
    /// <returns>The options and the JSON names of the options that were left out, or a validation error.</returns>
    public static (Result<TOptions> Options, IReadOnlySet<string> Deferred) BindForValidation<TOptions>(JsonElement with)
        where TOptions : class
    {
        var element = Normalize(with);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (BindCore<TOptions>(element, new HashSet<string>()), new HashSet<string>());
        }

        var typed = TypedProperties<TOptions>();
        var node = JsonNode.Parse(element.GetRawText())!.AsObject();
        var deferred = node
            .Where(pair => typed.ContainsKey(pair.Key) && pair.Value is JsonValue value
                && value.TryGetValue<string>(out var text) && ExpressionResolver.IsSingleExpression(text))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in deferred)
        {
            node.Remove(name);
        }

        var skipped = deferred.Select(name => typed[name]).ToHashSet(StringComparer.Ordinal);
        return (BindCore<TOptions>(JsonSerializer.SerializeToElement(node), skipped), deferred);
    }

    private static JsonElement Normalize(JsonElement with) =>
        with.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? JsonSerializer.SerializeToElement(new Dictionary<string, object?>())
            : with;

    /// <summary>Binds the options, and ignores the rules of the given members.</summary>
    private static Result<TOptions> BindCore<TOptions>(JsonElement element, IReadOnlySet<string> skippedMembers)
        where TOptions : class
    {
        try
        {
            var options = element.Deserialize<TOptions>(_json);
            if (options is null)
            {
                return Invalid<TOptions>("The options of the step are empty.");
            }

            var problems = new List<ValidationResult>();
            Validator.TryValidateObject(options, new ValidationContext(options), problems, validateAllProperties: true);
            var relevant = problems.Where(problem => !problem.MemberNames.Any(skippedMembers.Contains)).ToList();
            return relevant.Count == 0
                ? Result<TOptions>.Ok(options)
                : Invalid<TOptions>(string.Join(" ", relevant.Select(problem => problem.ErrorMessage)));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Invalid<TOptions>($"The options of the step are not valid. {exception.Message}");
        }
    }

    /// <summary>Maps the JSON name of each option that is not text to the name of its property.</summary>
    private static Dictionary<string, string> TypedProperties<TOptions>()
    {
        var info = _json.GetTypeInfo(typeof(TOptions));
        return info.Properties
            .Where(property => property.PropertyType != typeof(string) && property.AttributeProvider is MemberInfo)
            .ToDictionary(property => property.Name, property => ((MemberInfo)property.AttributeProvider!).Name, StringComparer.OrdinalIgnoreCase);
    }

    private static Result<TOptions> Invalid<TOptions>(string message)
        where TOptions : class =>
        Result<TOptions>.Fail(new PipelineError("step.options_invalid", message, ErrorKind.Validation));
}
