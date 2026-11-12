using System.Net;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Options.Upstream;
using Xat.Orchestrator.Infrastructure.Options.Webhooks;

namespace Xat.Orchestrator.Infrastructure.Webhooks;

/// <summary>
/// Decides to which addresses the orchestrator sends webhooks. The rule allows only http and https, refuses credentials in the address,
/// and allows a host only when it is loopback or in <c>Webhooks:AllowedHosts</c>. The value <c>*</c> in the list allows every host.
/// The error message never repeats the address, because it can hold a secret.
/// An address of this machine on a port of the orchestrator or of an upstream service is always refused, also with <c>*</c>.
/// Otherwise a run could send its webhook to a route of xat-rag that the operator turned off, or to the orchestrator itself.
/// </summary>
/// <param name="options">The webhook settings.</param>
/// <param name="upstreams">The upstream settings, for their ports. A test can leave them out.</param>
/// <param name="listen">The addresses of the orchestrator. A test can leave them out.</param>
internal sealed class WebhookPolicy(
    IOptions<WebhookOptions> options,
    IOptions<UpstreamOptions>? upstreams = null,
    IListenAddresses? listen = null) : IWebhookPolicy
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

        if (ReachesThisMachine(uri) && ReservedPorts().Contains(uri.Port))
        {
            return Fail(
                "webhook.host_not_allowed",
                "The webhook address uses a port of the orchestrator or of an upstream service. Use the port of the receiver.");
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

    /// <summary>Gets the ports of the orchestrator and of the upstream services that have a fixed address.</summary>
    private HashSet<int> ReservedPorts()
    {
        var upstreamUrls = upstreams is null
            ? []
            : new[] { upstreams.Value.XatRag.BaseUrl, upstreams.Value.XatLlmEngine.BaseUrl }
                .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : null)
                .OfType<Uri>();
        return upstreamUrls.Concat(listen?.Current ?? []).Select(address => address.Port).ToHashSet();
    }

    /// <summary>
    /// Shows whether the address reaches this machine. That is a loopback address, or an address that means "any", such as
    /// <c>0.0.0.0</c> and <c>[::]</c>, which a connection on macOS and Linux sends to the machine itself. A host name that
    /// resolves to this machine is not caught here; the allow-list of the operator controls host names.
    /// </summary>
    private static bool ReachesThisMachine(Uri uri) =>
        IsLoopback(uri)
        || string.Equals(uri.IdnHost.TrimEnd('.'), "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.IdnHost, out var address) && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)));

    private static bool IsLoopback(Uri uri) =>
        string.Equals(uri.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.IdnHost, out var address) && IPAddress.IsLoopback(address));

    private static Result<Uri> Fail(string code, string message) =>
        Result<Uri>.Fail(new PipelineError(code, message, ErrorKind.Validation));
}
