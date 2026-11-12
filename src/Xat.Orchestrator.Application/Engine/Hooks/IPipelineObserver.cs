using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine.Hooks;

/// <summary>A hook that watches runs. A hook cannot change the flow. The engine logs the error of a hook and goes on.</summary>
public interface IPipelineObserver
{
    /// <summary>Called when a run starts.</summary>
    /// <param name="context">The context of the run.</param>
    /// <param name="ct">The token that cancels the run.</param>
    /// <returns>A task.</returns>
    ValueTask OnRunStartedAsync(PipelineContext context, CancellationToken ct) => ValueTask.CompletedTask;

    /// <summary>Called when a step starts.</summary>
    /// <param name="context">The run and the step.</param>
    /// <param name="ct">The token that cancels the run.</param>
    /// <returns>A task.</returns>
    ValueTask OnStepStartedAsync(StepContext context, CancellationToken ct) => ValueTask.CompletedTask;

    /// <summary>Called when a step ends, with success or with a failure.</summary>
    /// <param name="context">The run and the step.</param>
    /// <param name="outcome">The outcome of the step.</param>
    /// <param name="duration">The time that the step used.</param>
    /// <param name="ct">The token that cancels the run.</param>
    /// <returns>A task.</returns>
    ValueTask OnStepCompletedAsync(StepContext context, StepOutcome outcome, TimeSpan duration, CancellationToken ct) =>
        ValueTask.CompletedTask;

    /// <summary>Called when a run ends.</summary>
    /// <param name="context">The context of the run.</param>
    /// <param name="result">The result or the error.</param>
    /// <param name="ct">The token that cancels the hook.</param>
    /// <returns>A task.</returns>
    ValueTask OnRunCompletedAsync(PipelineContext context, Result<RunResult> result, CancellationToken ct) =>
        ValueTask.CompletedTask;
}
