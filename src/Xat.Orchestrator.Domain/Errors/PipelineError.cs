namespace Xat.Orchestrator.Domain.Errors;

/// <summary>An expected failure of a run, a step or an upstream call.</summary>
/// <param name="Code">A stable, machine-readable code, for example <c>retrieval.timeout</c>.</param>
/// <param name="Message">A short text for a person. The text never repeats an input value.</param>
/// <param name="Kind">The class of the failure.</param>
/// <param name="RetryAfter">The time after which the caller can try again, or <see langword="null"/>.</param>
/// <param name="Source">The name of the step or the upstream that caused the failure, or <see langword="null"/>.</param>
public sealed record PipelineError(
    string Code,
    string Message,
    ErrorKind Kind,
    TimeSpan? RetryAfter = null,
    string? Source = null);
