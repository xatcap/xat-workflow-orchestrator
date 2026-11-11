namespace Xat.Orchestrator.Domain.Runs;

/// <summary>The result of the webhook delivery of a run.</summary>
/// <param name="State">The state of the delivery.</param>
/// <param name="Attempts">The number of tries, including the first try.</param>
/// <param name="Error">A short text that tells why the delivery failed, or <see langword="null"/>.</param>
public sealed record WebhookDelivery(WebhookState State, int Attempts, string? Error)
{
    /// <summary>Gets the value for a run that has no webhook.</summary>
    public static WebhookDelivery None { get; } = new(WebhookState.None, 0, null);

    /// <summary>Gets the value for a webhook that the service has not yet delivered.</summary>
    public static WebhookDelivery Pending { get; } = new(WebhookState.Pending, 0, null);
}
