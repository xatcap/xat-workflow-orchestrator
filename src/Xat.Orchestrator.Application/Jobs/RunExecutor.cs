using Microsoft.Extensions.Logging;
using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.Runs;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>
/// Executes the runs of the queue. An unexpected error never leaves the worker loop, and a webhook that was asked for
/// always goes to the delivery queue, also when the run failed in an unexpected way.
/// </summary>
internal sealed class RunExecutor(
    RunQueue queue,
    PipelineRunner runner,
    IRunStore store,
    WebhookDeliveryQueue deliveries,
    TimeProvider clock,
    ILogger logger)
{
    /// <summary>Takes runs from the queue and executes them, until the token stops the loop. The host calls it once for each run worker.</summary>
    /// <param name="stoppingToken">The token that stops the worker when the service stops.</param>
    /// <returns>A task that completes when the worker stops.</returns>
    public async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (item, cancel) in queue.ClaimAllAsync(stoppingToken))
            {
                await ProcessAsync(item, cancel.Token, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The service stops. Runs in the queue are lost, because the store is in memory.
        }
    }

    private async Task ProcessAsync(QueuedRun item, CancellationToken runToken, CancellationToken stoppingToken)
    {
        RunRecord? finished;
        try
        {
            finished = await ExecuteAsync(item, runToken, stoppingToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
        {
            AsyncRunLog.UnexpectedFailure(logger, exception, item.RunId);
            finished = store.Update(item.RunId, current => current.FailInternally(clock.GetUtcNow()));
        }
        finally
        {
            queue.Release(item.RunId);
        }

        if (finished is not null)
        {
            deliveries.Enqueue(item, finished);
        }
    }

    private async Task<RunRecord?> ExecuteAsync(QueuedRun item, CancellationToken runToken, CancellationToken stoppingToken)
    {
        var started = store.Update(item.RunId, current => current.Start(clock.GetUtcNow()));
        if (started is not { State: RunState.Running })
        {
            // The store removed the run. Nothing runs.
            return started;
        }

        var handle = runner.Start(item.Pipeline, item.Input, new RunRequest(Stream: false, item.RunId), runToken);
        var result = await handle.Completion;
        if (stoppingToken.IsCancellationRequested && !result.IsSuccess && result.Error.Kind == ErrorKind.Cancelled)
        {
            result = Result<RunResult>.Fail(new PipelineError(
                "run.service_stopped", "The service stopped before the run ended.", ErrorKind.Cancelled));
        }

        return store.Update(item.RunId, current => current.Finish(result, clock.GetUtcNow()));
    }
}
