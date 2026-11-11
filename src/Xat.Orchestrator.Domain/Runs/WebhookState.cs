namespace Xat.Orchestrator.Domain.Runs;

/// <summary>The state of the webhook delivery of a run.</summary>
public enum WebhookState
{
    /// <summary>The run has no webhook.</summary>
    None,

    /// <summary>The service has not yet sent the webhook, or it is sending it now.</summary>
    Pending,

    /// <summary>The receiver accepted the webhook.</summary>
    Delivered,

    /// <summary>The service could not deliver the webhook, also after the retries.</summary>
    Failed,
}
