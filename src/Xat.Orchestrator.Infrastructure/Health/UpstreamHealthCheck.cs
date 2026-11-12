using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Xat.Orchestrator.Infrastructure.Health;

/// <summary>
/// Checks that an upstream service is ready. A service that is down makes the orchestrator <c>degraded</c>, not <c>unhealthy</c>,
/// because the orchestrator itself still runs and answers requests that do not need the upstream.
/// </summary>
internal abstract class UpstreamHealthCheck(IHttpClientFactory factory, string clientName, string upstreamName) : IHealthCheck
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(3);

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_timeout);
        try
        {
            using var client = factory.CreateClient(clientName);
            using var response = await client.GetAsync("/health/ready", limit.Token);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"{upstreamName} is ready.")
                : new HealthCheckResult(context.Registration.FailureStatus, $"{upstreamName} is not ready. It answered with status {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"{upstreamName} is not reachable.");
        }
    }
}
