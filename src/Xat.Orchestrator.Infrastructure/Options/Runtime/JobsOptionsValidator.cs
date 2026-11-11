using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Jobs;

namespace Xat.Orchestrator.Infrastructure.Options.Runtime;

/// <summary>Checks the settings of the asynchronous runs when the service starts.</summary>
internal sealed class JobsOptionsValidator : IValidateOptions<JobsOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, JobsOptions options)
    {
        var failures = new List<string>();
        if (options.Workers is < 1 or > 32)
        {
            failures.Add($"{JobsOptions.SectionName}:Workers must be from 1 to 32.");
        }

        if (options.WebhookWorkers is < 1 or > 32)
        {
            failures.Add($"{JobsOptions.SectionName}:WebhookWorkers must be from 1 to 32.");
        }

        if (options.QueueLimit is < 1 or > 10_000)
        {
            failures.Add($"{JobsOptions.SectionName}:QueueLimit must be from 1 to 10000.");
        }

        if (options.RetainFinished is < 1 or > 100_000)
        {
            failures.Add($"{JobsOptions.SectionName}:RetainFinished must be from 1 to 100000.");
        }

        if (options.RetentionMinutes is < 1 or > 10_080)
        {
            failures.Add($"{JobsOptions.SectionName}:RetentionMinutes must be from 1 to 10080.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
