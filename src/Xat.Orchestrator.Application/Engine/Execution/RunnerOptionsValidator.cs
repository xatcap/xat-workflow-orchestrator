using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Configuration;

namespace Xat.Orchestrator.Application.Engine.Execution;

/// <summary>Checks the runner settings when the service starts.</summary>
internal sealed class RunnerOptionsValidator : IValidateOptions<RunnerOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, RunnerOptions options)
    {
        var failures = new List<string>();
        OptionRules.Check(options, RunnerOptions.SectionName, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
