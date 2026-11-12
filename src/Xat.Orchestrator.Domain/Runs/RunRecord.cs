using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Domain.Runs;

/// <summary>The stored state of an asynchronous run. A caller reads it with <c>GET /v1/runs/{id}</c>.</summary>
/// <param name="RunId">The ID of the run.</param>
/// <param name="Pipeline">The name of the pipeline.</param>
/// <param name="Version">The version of the pipeline.</param>
/// <param name="State">The state of the run.</param>
/// <param name="CreatedAt">The time at which the service accepted the run.</param>
/// <param name="StartedAt">The time at which a worker started the run, or <see langword="null"/>.</param>
/// <param name="CompletedAt">The time at which the run ended, or <see langword="null"/>.</param>
/// <param name="Result">The result of a successful run, or <see langword="null"/>.</param>
/// <param name="Error">The error of a failed or cancelled run, or <see langword="null"/>.</param>
/// <param name="WebhookTarget">Where the webhook goes, with the scheme, the host and the port only (no path and no query), or <see langword="null"/>.</param>
/// <param name="Webhook">The state of the webhook delivery.</param>
/// <param name="Debug">Shows whether the caller asked for the debug data in the result.</param>
public sealed record RunRecord(
    string RunId,
    string Pipeline,
    int Version,
    RunState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    RunResult? Result,
    PipelineError? Error,
    string? WebhookTarget,
    WebhookDelivery Webhook,
    bool Debug)
{
    /// <summary>Gets a value that shows whether the run ended.</summary>
    public bool IsFinished => State is RunState.Succeeded or RunState.Failed or RunState.Cancelled;

    /// <summary>Starts a queued run. A run in an other state does not change.</summary>
    /// <param name="now">The time now.</param>
    /// <returns>The changed run.</returns>
    public RunRecord Start(DateTimeOffset now) =>
        State == RunState.Queued ? this with { State = RunState.Running, StartedAt = now } : this;

    /// <summary>Ends a run with its result. A cancel error ends the run as cancelled. An other error ends it as failed.</summary>
    /// <param name="result">The result or the error of the run.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The changed run.</returns>
    public RunRecord Finish(Result<RunResult> result, DateTimeOffset now)
    {
        if (result.IsSuccess)
        {
            return this with { State = RunState.Succeeded, CompletedAt = now, Result = result.Value, Error = null };
        }

        var state = result.Error.Kind == ErrorKind.Cancelled ? RunState.Cancelled : RunState.Failed;
        return this with { State = state, CompletedAt = now, Error = result.Error };
    }

    /// <summary>Cancels a run that waits in the queue. A run in an other state does not change.</summary>
    /// <param name="now">The time now.</param>
    /// <returns>The changed run.</returns>
    public RunRecord CancelWhileQueued(DateTimeOffset now) =>
        State == RunState.Queued
            ? this with
            {
                State = RunState.Cancelled,
                CompletedAt = now,
                Error = new PipelineError("run.cancelled", "The caller cancelled the run.", ErrorKind.Cancelled),
            }
            : this;

    /// <summary>Ends a run that failed because of an unexpected error. A finished run does not change.</summary>
    /// <param name="now">The time now.</param>
    /// <returns>The changed run.</returns>
    public RunRecord FailInternally(DateTimeOffset now) =>
        IsFinished
            ? this
            : this with
            {
                State = RunState.Failed,
                CompletedAt = now,
                Error = new PipelineError("run.internal_error", "The run failed because of an internal error.", ErrorKind.Internal),
            };

    /// <summary>Marks the webhook as failed. The number of tries stays the same.</summary>
    /// <param name="reason">A short text that tells why.</param>
    /// <returns>The changed run.</returns>
    public RunRecord FailWebhook(string reason) =>
        this with { Webhook = new WebhookDelivery(WebhookState.Failed, Webhook.Attempts, reason) };
}
