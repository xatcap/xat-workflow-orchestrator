using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Infrastructure.Options.Upstream;

/// <summary>The address and the limits of one upstream service.</summary>
internal sealed class UpstreamEndpointOptions
{
    /// <summary>Gets the fixed base URL, for example <c>http://127.0.0.1:8420</c>. It must be a loopback address.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Gets the path of the ready file of the service. The file holds <c>{"url","pid","version"}</c>. Use it when the port changes at each start.</summary>
    public string? ReadyFile { get; init; }

    /// <summary>Gets the highest time of one request, with all retries, in seconds.</summary>
    [Range(1, 3600, ErrorMessage = "must be from 1 to 3600.")]
    public int RequestTimeoutSeconds { get; init; } = 120;

    /// <summary>Gets the number of retries after a 429, 503 or 504 answer or a failed connection.</summary>
    [Range(0, 5, ErrorMessage = "must be from 0 to 5.")]
    public int RetryCount { get; init; } = 2;
}
