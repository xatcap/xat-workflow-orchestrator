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
}
