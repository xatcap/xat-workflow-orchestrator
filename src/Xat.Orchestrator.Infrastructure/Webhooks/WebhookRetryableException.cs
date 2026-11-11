namespace Xat.Orchestrator.Infrastructure.Webhooks;

/// <summary>The receiver answered with a status that can change at the next try, for example 503. The retry pipeline handles it.</summary>
internal sealed class WebhookRetryableException(int status) : Exception($"The receiver answered with status {status}.")
{
    /// <summary>Gets the HTTP status of the answer.</summary>
    public int Status { get; } = status;
}
