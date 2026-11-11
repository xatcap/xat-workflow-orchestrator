using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Infrastructure.Discovery;

namespace Xat.Orchestrator.Infrastructure.Options;

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

            if (model.Parallel < 1)
            {
                failures.Add($"{prefix}:Parallel must be at least 1.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
