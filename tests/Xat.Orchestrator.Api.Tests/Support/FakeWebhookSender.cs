using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Tests.Support;

/// <summary>A fake webhook sender. A test sets the result and reads the calls.</summary>
internal sealed class FakeWebhookSender : IWebhookSender
{
    public List<(Uri Url, string RunId, string Body)> Calls { get; } = [];

    public WebhookDelivery Result { get; set; } = new(WebhookState.Delivered, 1, null);

    public Task<WebhookDelivery> DeliverAsync(Uri url, string runId, string jsonBody, CancellationToken ct)
    {
        lock (Calls)
        {
            Calls.Add((url, runId, jsonBody));
        }

        return Task.FromResult(Result);
    }
}
