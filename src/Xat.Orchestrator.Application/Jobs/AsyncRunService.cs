using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.Inputs;
using Xat.Orchestrator.Application.Engine.Runs;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>
/// Runs pipelines in the background. A caller submits a run and gets its ID at once, then reads the result later,
/// or receives it as a webhook. The service keeps the runs in memory, so a restart loses them.
/// The work has three parts: <see cref="RunQueue"/> holds the waiting and running runs, <see cref="RunExecutor"/> executes them,
/// and <see cref="WebhookDeliveryQueue"/> sends the webhooks.
/// </summary>
public sealed class AsyncRunService
{
    private readonly IRunStore _store;
    private readonly IWebhookPolicy _webhookPolicy;
    private readonly TimeProvider _clock;
    private readonly RunQueue _queue;
    private readonly RunExecutor _executor;
    private readonly WebhookDeliveryQueue _deliveries;

    /// <summary>Initializes a new instance of the <see cref="AsyncRunService"/> class.</summary>
    /// <param name="runner">The pipeline runner.</param>
    /// <param name="store">The store of the runs.</param>
    /// <param name="webhookSender">The sender of the webhooks.</param>
    /// <param name="webhookPolicy">The rule for the webhook addresses.</param>
    /// <param name="payloadWriter">The writer of the webhook body.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings of the asynchronous runs.</param>
    /// <param name="logger">The logger.</param>
    public AsyncRunService(
        PipelineRunner runner,
        IRunStore store,
        IWebhookSender webhookSender,
        IWebhookPolicy webhookPolicy,
        IWebhookPayloadWriter payloadWriter,
        TimeProvider clock,
        IOptions<JobsOptions> options,
        ILogger<AsyncRunService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _store = store;
        _webhookPolicy = webhookPolicy;
        _clock = clock;
        _queue = new RunQueue(Math.Max(1, options.Value.QueueLimit));
        _deliveries = new WebhookDeliveryQueue(store, webhookSender, payloadWriter, logger);
        _executor = new RunExecutor(_queue, runner, store, _deliveries, clock, logger);
    }

    /// <summary>Accepts a run. The run waits in the queue until a worker takes it.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="input">The input of the caller.</param>
    /// <param name="webhookUrl">The address of a webhook, or <see langword="null"/> for no webhook.</param>
    /// <param name="debug">Shows whether the result must hold the debug data.</param>
    /// <param name="stream">Shows whether the caller asked for a stream. An asynchronous run never streams.</param>
    /// <returns>The new run in the state <see cref="RunState.Queued"/>, or an error.</returns>
    public Result<RunRecord> Submit(PipelineDefinition pipeline, JsonElement input, string? webhookUrl, bool debug, bool stream = false)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        if (RunAdmission.CheckAsync(pipeline, stream) is { } streamError)
        {
            return Result<RunRecord>.Fail(streamError);
        }

        // Check the input now, so the caller gets the error at once and not in a later poll.
        if (InputSchemaValidator.Check(pipeline.InputSchema, input) is { } inputError)
        {
            return Result<RunRecord>.Fail(inputError);
        }

        Uri? webhook = null;
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            var checkedUrl = _webhookPolicy.Check(webhookUrl);
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
            _clock.GetUtcNow(),
            null,
            null,
            null,
            null,
            webhook is null ? null : WebhookDeliveryQueue.Describe(webhook),
            webhook is null ? WebhookDelivery.None : WebhookDelivery.Pending,
            debug);

        // The store gets the run first, because a worker can take the run from the queue at once.
        _store.Add(record);
        if (!_queue.TryEnqueue(new QueuedRun(runId, pipeline, input, webhook)))
        {
            _store.Remove(runId);
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
        return _store.TryGet(runId, out var record) ? Result<RunRecord>.Ok(record) : NotFound();
    }

    /// <summary>
    /// Asks a run to stop. A queued run changes to cancelled at once, it leaves the queue, and its webhook goes out.
    /// A running run stops soon after, and its state changes when the run ends. A finished run does not change.
    /// </summary>
    /// <param name="runId">The ID of the run.</param>
    /// <returns>The run as it is after the request, or a not-found error.</returns>
    public Result<RunRecord> Cancel(string runId)
    {
        ArgumentNullException.ThrowIfNull(runId);
        if (!_store.TryGet(runId, out var record))
        {
            return NotFound();
        }

        if (record.IsFinished)
        {
            return Result<RunRecord>.Ok(record);
        }

        if (_queue.Cancel(runId, out var removed) != RunQueue.CancelOutcome.RemovedFromQueue)
        {
            // The run executes and stops soon, or it ended at the same moment. Show the run as it is now.
            return Find(runId);
        }

        var cancelled = _store.Update(runId, current => current.CancelWhileQueued(_clock.GetUtcNow()));
        if (cancelled is null)
        {
            return NotFound();
        }

        _deliveries.Enqueue(removed!, cancelled);
        return Result<RunRecord>.Ok(cancelled);
    }

    /// <summary>Executes queued runs until the token stops the loop. The host calls this method once for each run worker.</summary>
    /// <param name="stoppingToken">The token that stops the worker when the service stops.</param>
    /// <returns>A task that completes when the worker stops.</returns>
    public Task RunWorkerAsync(CancellationToken stoppingToken) => _executor.RunWorkerAsync(stoppingToken);

    /// <summary>Sends webhooks until the token stops the loop. The host calls this method once for each delivery worker.</summary>
    /// <param name="stoppingToken">The token that stops the worker when the service stops.</param>
    /// <returns>A task that completes when the worker stops.</returns>
    public Task RunDeliveryWorkerAsync(CancellationToken stoppingToken) => _deliveries.RunWorkerAsync(stoppingToken);

    private static Result<RunRecord> NotFound() =>
        Result<RunRecord>.Fail(new PipelineError("run.not_found", "The run does not exist, or the service removed it.", ErrorKind.NotFound));
}
