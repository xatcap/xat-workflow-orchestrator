using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>
/// Runs pipelines in the background. A caller submits a run and gets its ID at once, then reads the result later,
/// or receives it as a webhook. The service keeps the runs in memory, so a restart loses them.
/// Run workers execute the pipelines. Separate delivery workers send the webhooks, so a slow receiver never blocks a run.
/// </summary>
public sealed class AsyncRunService(
    PipelineRunner runner,
    IRunStore store,
    IWebhookSender webhookSender,
    IWebhookPolicy webhookPolicy,
    IWebhookPayloadWriter payloadWriter,
    TimeProvider clock,
    IOptions<JobsOptions> options,
    ILogger<AsyncRunService> logger)
{
    /// <summary>The number of finished runs that can wait for the delivery of their webhook.</summary>
    private const int DeliveryQueueCapacity = 1000;

    private readonly Channel<QueuedRun> _queue = Channel.CreateBounded<QueuedRun>(new BoundedChannelOptions(Math.Max(1, options.Value.QueueLimit))
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
    });

    private readonly Channel<PendingDelivery> _deliveries = Channel.CreateBounded<PendingDelivery>(new BoundedChannelOptions(DeliveryQueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
    });

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);

    /// <summary>Accepts a run. The run waits in the queue until a worker takes it.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="input">The input of the caller.</param>
    /// <param name="webhookUrl">The address of a webhook, or <see langword="null"/> for no webhook.</param>
    /// <param name="debug">Shows whether the result must hold the debug data.</param>
    /// <returns>The new run in the state <see cref="RunState.Queued"/>, or an error.</returns>
    public Result<RunRecord> Submit(PipelineDefinition pipeline, JsonElement input, string? webhookUrl, bool debug)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        // An asynchronous run never streams, so a pipeline that always streams cannot run here.
        if (pipeline.Stream == StreamMode.Required)
        {
            return Result<RunRecord>.Fail(new PipelineError(
                "stream.not_supported_async",
                "This pipeline sends a stream only, so it cannot run in asynchronous mode.",
                ErrorKind.Validation));
        }

        // Check the input now, so the caller gets the error at once and not in a later poll.
        var problems = InputSchemaValidator.Validate(pipeline.InputSchema, input);
        if (problems.Count > 0)
        {
            return Result<RunRecord>.Fail(new PipelineError("input.invalid", string.Join(" ", problems), ErrorKind.Validation));
        }

        Uri? webhook = null;
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            var checkedUrl = webhookPolicy.Check(webhookUrl);
            if (!checkedUrl.IsSuccess)
            {
                return Result<RunRecord>.Fail(checkedUrl.Error);
            }

            webhook = checkedUrl.Value;
        }

        var runId = RunIds.New();
        var record = new RunRecord(
            runId,
            pipeline.Name,
            pipeline.Version,
            RunState.Queued,
            clock.GetUtcNow(),
            null,
            null,
            null,
            null,
            webhook is null ? null : DescribeTarget(webhook),
            webhook is null ? WebhookDelivery.None : WebhookDelivery.Pending,
            debug);

        // The store gets the run first, because a worker can take the run from the queue at once.
        store.Add(record);
        if (!_queue.Writer.TryWrite(new QueuedRun(runId, pipeline, input, webhook)))
        {
            store.Remove(runId);
            return Result<RunRecord>.Fail(new PipelineError(
                "jobs.queue_full",
                "The queue of asynchronous runs is full. Try again later.",
                ErrorKind.Overloaded,
                TimeSpan.FromSeconds(5)));
        }

        return Result<RunRecord>.Ok(record);
    }

    /// <summary>Finds a run.</summary>
    /// <param name="runId">The ID of the run.</param>
    /// <returns>The run, or a not-found error. The store removes old finished runs, so a late call can get this error.</returns>
    public Result<RunRecord> Find(string runId)
    {
        ArgumentNullException.ThrowIfNull(runId);
        return store.TryGet(runId, out var record) ? Result<RunRecord>.Ok(record) : NotFound();
    }

    /// <summary>
    /// Asks a run to stop. A queued run changes to cancelled at once. A running run stops soon after, and its state changes
    /// when the run ends. A finished run does not change.
    /// </summary>
    /// <param name="runId">The ID of the run.</param>
    /// <returns>The run as it is after the request, or a not-found error.</returns>
    public Result<RunRecord> Cancel(string runId)
    {
        ArgumentNullException.ThrowIfNull(runId);
        if (!store.TryGet(runId, out var record))
        {
            return NotFound();
        }

        if (record.IsFinished)
        {
            return Result<RunRecord>.Ok(record);
        }

        if (TryCancelRunning(runId))
        {
            return Result<RunRecord>.Ok(record);
        }

        var updated = store.Update(runId, current => current.State == RunState.Queued
            ? current with
            {
                State = RunState.Cancelled,
                CompletedAt = clock.GetUtcNow(),
                Error = new PipelineError("run.cancelled", "The caller cancelled the run.", ErrorKind.Cancelled),
            }
            : current);

        // A worker can take the run between the first check and the update. Then the run is running, and the cancel goes to it.
        if (updated is { State: RunState.Running })
        {
            TryCancelRunning(runId);
        }

        return updated is null ? NotFound() : Result<RunRecord>.Ok(updated);
    }

    /// <summary>Takes runs from the queue and executes them, until the token stops the loop. The host calls this method once for each run worker.</summary>
    /// <param name="stoppingToken">The token that stops the worker when the service stops.</param>
    /// <returns>A task that completes when the worker stops.</returns>
    public async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessAsync(item, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The service stops. Runs in the queue are lost, because the store is in memory.
        }
    }

    /// <summary>Takes finished runs from the delivery queue and sends their webhooks, until the token stops the loop. The host calls this method once for each delivery worker.</summary>
    /// <param name="stoppingToken">The token that stops the worker when the service stops.</param>
    /// <returns>A task that completes when the worker stops.</returns>
    public async Task RunDeliveryWorkerAsync(CancellationToken stoppingToken)
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

    /// <summary>
    /// Executes one run. Whatever happens, an unexpected error never leaves the worker loop, and a webhook that was asked for
    /// always goes to the delivery queue, also when the run failed in an unexpected way.
    /// </summary>
    private async Task ProcessAsync(QueuedRun item, CancellationToken stoppingToken)
    {
        RunRecord? finished;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        // The worker registers its token before it changes the state, so a cancel that comes in between always finds it.
        _running[item.RunId] = cts;
        try
        {
            finished = await ExecuteAsync(item, cts, stoppingToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
        {
            AsyncRunLog.UnexpectedFailure(logger, exception, item.RunId);
            finished = FailUnexpected(item.RunId);
        }
        finally
        {
            _running.TryRemove(item.RunId, out _);
        }

        if (finished is not null)
        {
            EnqueueDelivery(item, finished);
        }
    }

    private async Task<RunRecord?> ExecuteAsync(QueuedRun item, CancellationTokenSource cts, CancellationToken stoppingToken)
    {
        var started = store.Update(item.RunId, current => current.State == RunState.Queued
            ? current with { State = RunState.Running, StartedAt = clock.GetUtcNow() }
            : current);

        if (started is not { State: RunState.Running })
        {
            // The caller cancelled the run while it waited, or the store removed it. Nothing runs, and the webhook still reports the end.
            return started;
        }

        var handle = runner.Start(item.Pipeline, item.Input, new RunRequest(Stream: false, item.RunId), cts.Token);
        var result = await handle.Completion;
        if (stoppingToken.IsCancellationRequested && !result.IsSuccess && result.Error.Kind == ErrorKind.Cancelled)
        {
            result = Result<RunResult>.Fail(new PipelineError(
                "run.service_stopped", "The service stopped before the run ended.", ErrorKind.Cancelled));
        }

        return store.Update(item.RunId, current => Finish(current, result));
    }

    private RunRecord Finish(RunRecord current, Result<RunResult> result)
    {
        var now = clock.GetUtcNow();
        if (result.IsSuccess)
        {
            return current with { State = RunState.Succeeded, CompletedAt = now, Result = result.Value, Error = null };
        }

        var state = result.Error.Kind == ErrorKind.Cancelled ? RunState.Cancelled : RunState.Failed;
        return current with { State = state, CompletedAt = now, Error = result.Error };
    }

    private RunRecord? FailUnexpected(string runId) =>
        store.Update(runId, current => current.IsFinished
            ? current
            : current with
            {
                State = RunState.Failed,
                CompletedAt = clock.GetUtcNow(),
                Error = new PipelineError("run.internal_error", "The run failed because of an internal error.", ErrorKind.Internal),
            });

    private void EnqueueDelivery(QueuedRun item, RunRecord finished)
    {
        if (item.Webhook is null)
        {
            return;
        }

        if (!_deliveries.Writer.TryWrite(new PendingDelivery(item, finished)))
        {
            MarkWebhookFailed(item.RunId, "The delivery queue is full.");
            AsyncRunLog.WebhookFailed(logger, item.RunId, "The delivery queue is full.");
        }
    }

    private async Task DeliverAsync(PendingDelivery delivery, CancellationToken ct)
    {
        var runId = delivery.Item.RunId;
        try
        {
            var body = payloadWriter.Write(delivery.Finished with { Webhook = WebhookDelivery.Pending });
            var result = await webhookSender.DeliverAsync(delivery.Item.Webhook!, runId, body, ct);
            store.Update(runId, current => current with { Webhook = result });
            if (result.State == WebhookState.Failed)
            {
                AsyncRunLog.WebhookFailed(logger, runId, result.Error ?? "No reason.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            MarkWebhookFailed(runId, "The service stopped before the delivery ended.");
            throw;
        }
        catch (Exception exception)
        {
            // A webhook must never stay pending for ever, so any other failure ends it as failed.
            AsyncRunLog.WebhookUnexpectedFailure(logger, exception, runId);
            MarkWebhookFailed(runId, "The delivery failed because of an internal error.");
        }
    }

    private void MarkWebhookFailed(string runId, string reason) =>
        store.Update(runId, current => current with { Webhook = new WebhookDelivery(WebhookState.Failed, current.Webhook.Attempts, reason) });

    private bool TryCancelRunning(string runId)
    {
        if (!_running.TryGetValue(runId, out var cts))
        {
            return false;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run ended at the same moment. It is finished, so there is nothing to cancel.
        }

        return true;
    }

    /// <summary>
    /// Shows where the webhook goes, with the scheme, the host and the port only. The path and the query are not shown,
    /// because many receivers put a secret in the path.
    /// </summary>
    private static string DescribeTarget(Uri webhook) => $"{webhook.Scheme}://{webhook.Authority}";

    private static Result<RunRecord> NotFound() =>
        Result<RunRecord>.Fail(new PipelineError("run.not_found", "The run does not exist, or the service removed it.", ErrorKind.NotFound));
}
