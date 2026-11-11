using Microsoft.Extensions.Options;

namespace Xat.Orchestrator.Infrastructure.Options.Webhooks;

/// <summary>Checks the webhook settings when the service starts.</summary>
internal sealed class WebhookOptionsValidator : IValidateOptions<WebhookOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, WebhookOptions options)
    {
        var failures = new List<string>();
        if (options.TimeoutSeconds is < 1 or > 120)
        {
            failures.Add($"{WebhookOptions.SectionName}:TimeoutSeconds must be from 1 to 120.");
        }

        if (options.RetryCount is < 0 or > 10)
        {
            failures.Add($"{WebhookOptions.SectionName}:RetryCount must be from 0 to 10.");
        }

        if (options.RetryDelayMilliseconds is < 0 or > 60_000)
        {
            failures.Add($"{WebhookOptions.SectionName}:RetryDelayMilliseconds must be from 0 to 60000.");
        }

        if (options.AllowedHosts.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{WebhookOptions.SectionName}:AllowedHosts must not hold an empty entry.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
