using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Configuration;

namespace Xat.Orchestrator.Infrastructure.Options.Retrieval;

/// <summary>Checks the retrieval aliases when the service starts.</summary>
internal sealed partial class RetrievalOptionsValidator : IValidateOptions<RetrievalOptions>
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}\\z")]
    private static partial Regex NamePattern();

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, RetrievalOptions options)
    {
        var failures = new List<string>();
        foreach (var (alias, profile) in options.Profiles)
        {
            if (!NamePattern().IsMatch(alias))
            {
                failures.Add($"{RetrievalOptions.SectionName}:Profiles:{alias}: the alias must match [a-z0-9][a-z0-9-]{{0,63}}.");
            }

            if (!NamePattern().IsMatch(profile.Context))
            {
                failures.Add($"{RetrievalOptions.SectionName}:Profiles:{alias}:Context must be a xat-rag context name.");
            }

            OptionRules.Check(profile, $"{RetrievalOptions.SectionName}:Profiles:{alias}", failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
