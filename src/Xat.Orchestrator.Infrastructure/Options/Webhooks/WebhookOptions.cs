namespace Xat.Orchestrator.Infrastructure.Options.Webhooks;

/// <summary>The settings of the webhooks.</summary>
internal sealed class WebhookOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Webhooks";

    /// <summary>
    /// Gets the host names to which the orchestrator can send webhooks. An empty list means loopback hosts only.
    /// The value <c>*</c> allows every host. This list limits where the orchestrator sends requests. It is not authentication.
    /// </summary>
    public List<string> AllowedHosts { get; } = [];

    /// <summary>Gets the highest time of one delivery try, in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>Gets the number of retries after a failed try.</summary>
    public int RetryCount { get; init; } = 3;

    /// <summary>Gets the base delay before the first retry, in milliseconds. The delay grows for each retry.</summary>
    public int RetryDelayMilliseconds { get; init; } = 1000;
}
