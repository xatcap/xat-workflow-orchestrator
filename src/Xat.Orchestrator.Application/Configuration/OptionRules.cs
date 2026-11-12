using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Application.Configuration;

/// <summary>
/// Checks the rules that the attributes of a settings class set, such as <see cref="RangeAttribute"/>. Each validator calls it,
/// so a range lives in one place: the attribute. The message names the full key, for example <c>Upstreams:XatRag:RetryCount</c>.
/// </summary>
public static class OptionRules
{
    /// <summary>Checks the attributes of the properties of a settings object.</summary>
    /// <param name="options">The settings object.</param>
    /// <param name="keyPrefix">The configuration key of the object, for example <c>Upstreams:XatRag</c>.</param>
    /// <param name="failures">The list that gets one message for each broken rule.</param>
    public static void Check(object options, string keyPrefix, List<string> failures)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(failures);

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        failures.AddRange(results.Select(result => $"{keyPrefix}:{result.MemberNames.FirstOrDefault()} {result.ErrorMessage}"));
    }
}
