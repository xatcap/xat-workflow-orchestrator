using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Configuration;

namespace Xat.Orchestrator.Infrastructure.Options.Webhooks;

/// <summary>Checks the webhook settings when the service starts.</summary>
internal sealed class WebhookOptionsValidator : IValidateOptions<WebhookOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, WebhookOptions options)
    {
        var failures = new List<string>();
        OptionRules.Check(options, WebhookOptions.SectionName, failures);

        if (options.AllowedHosts.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{WebhookOptions.SectionName}:AllowedHosts must not hold an empty entry.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
