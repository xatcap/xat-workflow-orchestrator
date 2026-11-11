namespace Xat.Orchestrator.Api.Contracts;

/// <summary>The state of the webhook of a run.</summary>
/// <param name="Url">Where the webhook goes, with the scheme, the host and the port only. The path and the query are not shown, because they can hold a secret.</param>
/// <param name="Status">One of <c>pending</c>, <c>delivered</c> and <c>failed</c>.</param>
/// <param name="Attempts">The number of delivery tries.</param>
/// <param name="Error">A short text that tells why the delivery failed, or <see langword="null"/>.</param>
internal sealed record WebhookDto(string Url, string Status, int Attempts, string? Error);
