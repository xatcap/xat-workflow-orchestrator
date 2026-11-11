using System.Net;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Options.Webhooks;

namespace Xat.Orchestrator.Infrastructure.Webhooks;

/// <summary>
/// Decides to which addresses the orchestrator sends webhooks. The rule allows only http and https, refuses credentials in the address,
/// and allows a host only when it is loopback or in <c>Webhooks:AllowedHosts</c>. The value <c>*</c> in the list allows every host.
/// The error message never repeats the address, because it can hold a secret.
/// </summary>
internal sealed class WebhookPolicy(IOptions<WebhookOptions> options) : IWebhookPolicy
{
    /// <inheritdoc/>
    public Result<Uri> Check(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return Fail("webhook.invalid_url", "The webhook address must be an absolute http or https address.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return Fail("webhook.invalid_url", "The webhook address must not hold a user name or a password.");
        }

        var allowed = options.Value.AllowedHosts;
        if (allowed.Contains("*", StringComparer.Ordinal) || IsLoopback(uri) || allowed.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
        {
            return Result<Uri>.Ok(uri);
        }

        return Fail(
            "webhook.host_not_allowed",
            "The webhook host is not allowed. An operator can allow it with Webhooks:AllowedHosts.");
    }

    private static bool IsLoopback(Uri uri) =>
        string.Equals(uri.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.IdnHost, out var address) && IPAddress.IsLoopback(address));

    private static Result<Uri> Fail(string code, string message) =>
        Result<Uri>.Fail(new PipelineError(code, message, ErrorKind.Validation));
}
