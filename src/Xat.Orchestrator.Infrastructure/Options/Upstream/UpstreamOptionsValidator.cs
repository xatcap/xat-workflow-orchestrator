using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Configuration;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Infrastructure.Discovery;

namespace Xat.Orchestrator.Infrastructure.Options.Upstream;

/// <summary>
/// Checks the upstream settings when the service starts. It also warns when the time limit of an upstream call is not shorter
/// than the time limit of a run, because the run timeout then always comes first and the upstream timeout error never shows.
/// </summary>
/// <param name="runs">The runner settings, for the warning. A test can leave them out.</param>
/// <param name="logger">The logger, for the warning. A test can leave it out.</param>
internal sealed partial class UpstreamOptionsValidator(
    IOptions<RunnerOptions>? runs = null,
    ILogger<UpstreamOptionsValidator>? logger = null) : IValidateOptions<UpstreamOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, UpstreamOptions options)
    {
        var failures = new List<string>();
        Check("XatRag", options.XatRag, failures);
        Check("XatLlmEngine", options.XatLlmEngine, failures);
        WarnAboutTimeout("XatRag", options.XatRag);
        WarnAboutTimeout("XatLlmEngine", options.XatLlmEngine);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Key}:RequestTimeoutSeconds is {Seconds} s, which is not shorter than Runs:RunTimeout ({RunSeconds} s). The run timeout comes first.")]
    private static partial void TimeoutNotShorter(ILogger logger, string key, int seconds, double runSeconds);

    private void WarnAboutTimeout(string key, UpstreamEndpointOptions endpoint)
    {
        if (runs is null || logger is null || TimeSpan.FromSeconds(endpoint.RequestTimeoutSeconds) < runs.Value.RunTimeout)
        {
            return;
        }

        TimeoutNotShorter(logger, $"{UpstreamOptions.SectionName}:{key}", endpoint.RequestTimeoutSeconds, runs.Value.RunTimeout.TotalSeconds);
    }

    private static void Check(string key, UpstreamEndpointOptions endpoint, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl) && string.IsNullOrWhiteSpace(endpoint.ReadyFile))
        {
            failures.Add($"{UpstreamOptions.SectionName}:{key} needs BaseUrl or ReadyFile.");
        }

        OptionRules.Check(endpoint, $"{UpstreamOptions.SectionName}:{key}", failures);

        if (!string.IsNullOrWhiteSpace(endpoint.BaseUrl) && !LoopbackUrl.TryParse(endpoint.BaseUrl, out _))
        {
            failures.Add($"{UpstreamOptions.SectionName}:{key}:BaseUrl must be an http or https loopback address with no path, for example http://127.0.0.1:8421. The upstream services have no authentication.");
        }
    }
}
