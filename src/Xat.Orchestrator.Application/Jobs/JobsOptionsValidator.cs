using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Configuration;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>Checks the settings of the asynchronous runs when the service starts.</summary>
internal sealed class JobsOptionsValidator : IValidateOptions<JobsOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, JobsOptions options)
    {
        var failures = new List<string>();
        OptionRules.Check(options, JobsOptions.SectionName, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
