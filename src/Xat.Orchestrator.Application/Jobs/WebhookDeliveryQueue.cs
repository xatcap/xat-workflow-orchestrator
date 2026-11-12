using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>
/// Sends the webhooks of finished runs. It has its own queue and its own workers, so a slow receiver never blocks a run.
/// A failed delivery ends as failed. When the service stops, the webhooks in the queue are lost and stay pending,
/// like the runs, because the store is in memory.
/// </summary>
internal sealed class WebhookDeliveryQueue(
    IRunStore store,
    IWebhookSender sender,
    IWebhookPayloadWriter payloadWriter,
    ILogger logger)
{
    /// <summary>The number of finished runs that can wait for the delivery of their webhook.</summary>
    private const int Capacity = 1000;

    private readonly Channel<PendingDelivery> _deliveries = Channel.CreateBounded<PendingDelivery>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
    });

    /// <summary>
    /// Shows where a webhook goes, with the scheme, the host and the port only. The path and the query are not shown,
    /// because many receivers put a secret in the path.
    /// </summary>
    /// <param name="webhook">The address of the webhook.</param>
    /// <returns>The text for the stored run.</returns>
    public static string Describe(Uri webhook) => $"{webhook.Scheme}://{webhook.Authority}";

    /// <summary>Adds the webhook of a finished run to the queue. A run with no webhook adds nothing.</summary>
    /// <param name="item">The run, with the address of the webhook.</param>
    /// <param name="finished">The run in its final state.</param>
    public void Enqueue(QueuedRun item, RunRecord finished)
    {
        if (item.Webhook is null)
        {
            return;
        }

        if (!_deliveries.Writer.TryWrite(new PendingDelivery(item, finished)))
        {
            store.Update(item.RunId, current => current.FailWebhook("The delivery queue is full."));
            AsyncRunLog.WebhookFailed(logger, item.RunId, "The delivery queue is full.");
        }
    }

    /// <summary>Sends the webhooks in the queue, until the token stops the loop. The host calls it once for each delivery worker.</summary>
    /// <param name="stoppingToken">The token that stops the worker when the service stops.</param>
    /// <returns>A task that completes when the worker stops.</returns>
    public async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var delivery in _deliveries.Reader.ReadAllAsync(stoppingToken))
            {
                await DeliverAsync(delivery, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The service stops. Webhooks in the queue are lost.
        }
    }

    private async Task DeliverAsync(PendingDelivery delivery, CancellationToken ct)
    {
        var runId = delivery.Item.RunId;
        try
        {
            var body = payloadWriter.Write(delivery.Finished with { Webhook = WebhookDelivery.Pending });
            var result = await sender.DeliverAsync(delivery.Item.Webhook!, runId, body, ct);
            store.Update(runId, current => current with { Webhook = result });
            if (result.State == WebhookState.Failed)
            {
                AsyncRunLog.WebhookFailed(logger, runId, result.Error ?? "No reason.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            store.Update(runId, current => current.FailWebhook("The service stopped before the delivery ended."));
            throw;
        }
        catch (Exception exception)
        {
            AsyncRunLog.WebhookUnexpectedFailure(logger, exception, runId);
            store.Update(runId, current => current.FailWebhook("The delivery failed because of an internal error."));
        }
    }
}
