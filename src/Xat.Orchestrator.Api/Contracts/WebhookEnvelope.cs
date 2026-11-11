namespace Xat.Orchestrator.Api.Contracts;

/// <summary>The body of a webhook.</summary>
/// <param name="Event">The name of the event. The value is <c>run.finished</c>.</param>
/// <param name="Run">The run, in the same form as <c>GET /v1/runs/{id}</c>.</param>
internal sealed record WebhookEnvelope(string Event, RunStatusDto Run);
