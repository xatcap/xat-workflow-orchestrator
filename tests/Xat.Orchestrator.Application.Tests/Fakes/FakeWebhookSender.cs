using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Tests.Fakes;

/// <summary>A fake webhook sender. A test sets the result and reads the calls.</summary>
internal sealed class FakeWebhookSender : IWebhookSender
{
    public List<(Uri Url, string RunId, string Body)> Calls { get; } = [];

    public WebhookDelivery Result { get; set; } = new(WebhookState.Delivered, 1, null);

    /// <summary>Gets or sets a task that the sender waits for before it answers. A test uses it to simulate a receiver that hangs.</summary>
    public Task? Gate { get; set; }

    public async Task<WebhookDelivery> DeliverAsync(Uri url, string runId, string jsonBody, CancellationToken ct)
    {
        lock (Calls)
        {
            Calls.Add((url, runId, jsonBody));
        }

        if (Gate is not null)
        {
            await Gate.WaitAsync(ct);
        }

        return Result;
    }
}
