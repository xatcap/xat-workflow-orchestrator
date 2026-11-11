using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Abstractions.Runs;

/// <summary>The port that sends a webhook. The adapter owns the retries and the time limits.</summary>
public interface IWebhookSender
{
    /// <summary>
    /// Sends a webhook with a JSON body. The method never throws for a failed delivery.
    /// It throws <see cref="OperationCanceledException"/> when the token cancels the delivery.
    /// </summary>
    /// <param name="url">The address of the receiver.</param>
    /// <param name="runId">The ID of the run, which the sender puts in a header.</param>
    /// <param name="jsonBody">The body.</param>
    /// <param name="ct">The token that cancels the delivery.</param>
    /// <returns>The result of the delivery.</returns>
    Task<WebhookDelivery> DeliverAsync(Uri url, string runId, string jsonBody, CancellationToken ct);
}
