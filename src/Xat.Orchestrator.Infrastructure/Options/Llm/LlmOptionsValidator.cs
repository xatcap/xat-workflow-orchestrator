using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Configuration;

namespace Xat.Orchestrator.Infrastructure.Options.Llm;

/// <summary>Checks the model aliases when the service starts.</summary>
internal sealed partial class LlmOptionsValidator : IValidateOptions<LlmOptions>
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}\\z")]
    private static partial Regex NamePattern();

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, LlmOptions options)
    {
        var failures = new List<string>();
        if (options.Models.Count == 0)
        {
            failures.Add($"{LlmOptions.SectionName}:Models must define at least one model.");
        }
        else if (!options.Models.ContainsKey(options.DefaultModel))
        {
            failures.Add($"{LlmOptions.SectionName}:DefaultModel names the alias '{options.DefaultModel}', which is not in Models.");
        }

        foreach (var (alias, model) in options.Models)
        {
            var prefix = $"{LlmOptions.SectionName}:Models:{alias}";
            if (!NamePattern().IsMatch(alias))
            {
                failures.Add($"{prefix}: the alias must match [a-z0-9][a-z0-9-]{{0,63}}.");
            }

            if (!model.Model.Contains('/', StringComparison.Ordinal))
            {
                failures.Add($"{prefix}:Model must have the form provider/model.");
            }

            OptionRules.Check(model, prefix, failures);
        }

        // The limit belongs to the model, so all aliases of one model must use the same limit.
        failures.AddRange(options.Models
            .GroupBy(pair => pair.Value.Model, StringComparer.Ordinal)
            .Where(group => group.Select(pair => pair.Value.Parallel).Distinct().Count() > 1)
            .Select(group => $"{LlmOptions.SectionName}:Models: the aliases {string.Join(", ", group.Select(pair => pair.Key))} use the model "
                + $"'{group.Key}' with different Parallel values. The limit is for the model, so use the same value."));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
