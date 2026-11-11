using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>Turns the <c>with</c> block of a step into an options object and checks it.</summary>
internal static class StepOptionsBinder
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Binds and validates the options.</summary>
    /// <typeparam name="TOptions">The class of the options.</typeparam>
    /// <param name="with">The <c>with</c> block as JSON. A missing block means an empty object.</param>
    /// <returns>The options, or a validation error.</returns>
    public static Result<TOptions> Bind<TOptions>(JsonElement with)
        where TOptions : class
    {
        try
        {
            var element = with.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? JsonSerializer.SerializeToElement(new Dictionary<string, object?>())
                : with;
            var options = element.Deserialize<TOptions>(_json);
            if (options is null)
            {
                return Invalid<TOptions>("The options of the step are empty.");
            }

            var problems = new List<ValidationResult>();
            if (!Validator.TryValidateObject(options, new ValidationContext(options), problems, validateAllProperties: true))
            {
                return Invalid<TOptions>(string.Join(" ", problems.Select(problem => problem.ErrorMessage)));
            }

            return Result<TOptions>.Ok(options);
        }
        catch (JsonException exception)
        {
            return Invalid<TOptions>($"The options of the step are not valid. {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            return Invalid<TOptions>($"The options of the step are not valid. {exception.Message}");
        }
    }

    private static Result<TOptions> Invalid<TOptions>(string message)
        where TOptions : class =>
        Result<TOptions>.Fail(new PipelineError("step.options_invalid", message, ErrorKind.Validation));
}
