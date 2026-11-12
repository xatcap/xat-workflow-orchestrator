using Microsoft.Extensions.Logging;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine.Hooks;

/// <summary>
/// Writes one log line when a run starts, for each step, and when a run ends. The lines hold IDs, names, times, error codes and
/// token counts only. They never hold the input, the prompt, the chunks, the answer or an error message, because these can hold data.
/// The log level controls the lines: steps log at Debug, runs at Information, and failures at Warning.
/// </summary>
internal sealed partial class RunLogObserver(ILogger<RunLogObserver> logger) : IPipelineObserver
{
    /// <inheritdoc/>
    public ValueTask OnRunStartedAsync(PipelineContext context, CancellationToken ct)
    {
        RunStarted(logger, context.RunId, context.Pipeline.Name, context.Pipeline.Version);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask OnStepCompletedAsync(StepContext context, StepOutcome outcome, TimeSpan duration, CancellationToken ct)
    {
        var milliseconds = (long)duration.TotalMilliseconds;
        if (outcome is StepOutcome.FailedOutcome failed)
        {
            StepFailed(logger, context.Run.RunId, context.Step.Id, context.Step.Uses, milliseconds, failed.Error.Code);
        }
        else
        {
            StepCompleted(logger, context.Run.RunId, context.Step.Id, context.Step.Uses, milliseconds);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask OnRunCompletedAsync(PipelineContext context, Result<RunResult> result, CancellationToken ct)
    {
        var milliseconds = (long)context.Elapsed().TotalMilliseconds;
        var usage = context.State.Usage;
        if (result.IsSuccess)
        {
            RunCompleted(logger, context.RunId, result.Value.Status, milliseconds, usage.PromptTokens, usage.CompletionTokens);
        }
        else
        {
            RunFailed(logger, context.RunId, result.Error.Code, milliseconds, usage.PromptTokens, usage.CompletionTokens);
        }

        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} started. Pipeline {Pipeline}, version {Version}.")]
    private static partial void RunStarted(ILogger logger, string runId, string pipeline, int version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Run {RunId}: step {StepId} ({Uses}) ended in {DurationMs} ms.")]
    private static partial void StepCompleted(ILogger logger, string runId, string stepId, string uses, long durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId}: step {StepId} ({Uses}) failed in {DurationMs} ms with code {Code}.")]
    private static partial void StepFailed(ILogger logger, string runId, string stepId, string uses, long durationMs, string code);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Run {RunId} ended with status {Status} in {DurationMs} ms. Tokens: {PromptTokens} prompt, {CompletionTokens} completion.")]
    private static partial void RunCompleted(ILogger logger, string runId, RunStatus status, long durationMs, int promptTokens, int completionTokens);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Run {RunId} failed with code {Code} in {DurationMs} ms. Tokens: {PromptTokens} prompt, {CompletionTokens} completion.")]
    private static partial void RunFailed(ILogger logger, string runId, string code, long durationMs, int promptTokens, int completionTokens);
}
