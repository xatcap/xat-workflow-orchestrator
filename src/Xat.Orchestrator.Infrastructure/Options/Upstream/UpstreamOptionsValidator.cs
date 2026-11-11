using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Infrastructure.Discovery;

namespace Xat.Orchestrator.Infrastructure.Options.Upstream;

/// <summary>Checks the upstream settings when the service starts.</summary>
internal sealed class UpstreamOptionsValidator : IValidateOptions<UpstreamOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, UpstreamOptions options)
    {
        var failures = new List<string>();
        Check("XatRag", options.XatRag, failures);
        Check("XatLlmEngine", options.XatLlmEngine, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Check(string key, UpstreamEndpointOptions endpoint, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl) && string.IsNullOrWhiteSpace(endpoint.ReadyFile))
        {
            failures.Add($"{UpstreamOptions.SectionName}:{key} needs BaseUrl or ReadyFile.");
        }

        if (endpoint.RequestTimeoutSeconds is < 1 or > 3600)
        {
            failures.Add($"{UpstreamOptions.SectionName}:{key}:RequestTimeoutSeconds must be from 1 to 3600.");
        }

        if (endpoint.RetryCount is < 0 or > 5)
        {
            failures.Add($"{UpstreamOptions.SectionName}:{key}:RetryCount must be from 0 to 5.");
        }

        if (!string.IsNullOrWhiteSpace(endpoint.BaseUrl) && !LoopbackUrl.TryParse(endpoint.BaseUrl, out _))
        {
            failures.Add($"{UpstreamOptions.SectionName}:{key}:BaseUrl must be an http loopback address. The upstream services have no authentication.");
        }
    }
}
