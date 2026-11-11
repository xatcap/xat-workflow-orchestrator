using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts;

/// <summary>The state of an asynchronous run.</summary>
/// <param name="RunId">The ID of the run.</param>
/// <param name="Pipeline">The name of the pipeline.</param>
/// <param name="Version">The version of the pipeline.</param>
/// <param name="Status">One of <c>queued</c>, <c>running</c>, <c>succeeded</c>, <c>failed</c> and <c>cancelled</c>.</param>
/// <param name="CreatedAt">The time at which the service accepted the run.</param>
/// <param name="StartedAt">The time at which a worker started the run, or <see langword="null"/>.</param>
/// <param name="CompletedAt">The time at which the run ended, or <see langword="null"/>.</param>
/// <param name="Result">The result, when the status is <c>succeeded</c>. The form is the same as the form of a synchronous run.</param>
/// <param name="Error">The error, when the status is <c>failed</c> or <c>cancelled</c>.</param>
/// <param name="Webhook">The state of the webhook, or <see langword="null"/> when the run has no webhook.</param>
internal sealed record RunStatusDto(
    string RunId,
    string Pipeline,
    int Version,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    RunResponseDto? Result,
    ErrorBody? Error,
    WebhookDto? Webhook)
{
    /// <summary>Makes the DTO from a stored run.</summary>
    /// <param name="record">The run.</param>
    /// <returns>The DTO.</returns>
    public static RunStatusDto From(RunRecord record) =>
        new(
            record.RunId,
            record.Pipeline,
            record.Version,
            record.State.ToString().ToLowerInvariant(),
            record.CreatedAt,
            record.StartedAt,
            record.CompletedAt,
            record.Result is null ? null : RunResponseDto.From(record.Result, record.Debug),
            record.Error is null ? null : ApiErrors.ToEnvelope(record.Error, record.RunId).Error,
            record.Webhook.State == WebhookState.None
                ? null
                : new WebhookDto(record.WebhookTarget ?? string.Empty, record.Webhook.State.ToString().ToLowerInvariant(), record.Webhook.Attempts, record.Webhook.Error));
}
