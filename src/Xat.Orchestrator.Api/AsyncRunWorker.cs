using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Jobs;

namespace Xat.Orchestrator.Api;

/// <summary>Hosts the workers of the asynchronous runs. Run workers execute the pipelines. Delivery workers send the webhooks.</summary>
internal sealed class AsyncRunWorker(AsyncRunService service, IOptions<JobsOptions> options) : BackgroundService
{
    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(
            Enumerable.Range(0, options.Value.Workers).Select(_ => service.RunWorkerAsync(stoppingToken))
                .Concat(Enumerable.Range(0, options.Value.WebhookWorkers).Select(_ => service.RunDeliveryWorkerAsync(stoppingToken))));
}
