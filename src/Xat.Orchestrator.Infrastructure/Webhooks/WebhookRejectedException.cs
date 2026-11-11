namespace Xat.Orchestrator.Infrastructure.Webhooks;

/// <summary>The receiver refused the webhook with a status that a retry does not change, for example 400 or 404.</summary>
internal sealed class WebhookRejectedException(int status) : Exception($"The receiver answered with status {status}.")
{
    /// <summary>Gets the HTTP status of the answer.</summary>
    public int Status { get; } = status;
}
