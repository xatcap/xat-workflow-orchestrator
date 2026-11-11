using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Engine;

namespace Xat.Orchestrator.Infrastructure.Options;

/// <summary>Checks the runner settings when the service starts.</summary>
internal sealed class RunnerOptionsValidator : IValidateOptions<RunnerOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, RunnerOptions options) =>
        options.RunTimeout <= TimeSpan.Zero || options.RunTimeout > TimeSpan.FromHours(1)
            ? ValidateOptionsResult.Fail($"{RunnerOptions.SectionName}:RunTimeout must be more than 0 and at most 1 hour.")
            : ValidateOptionsResult.Success;
}
