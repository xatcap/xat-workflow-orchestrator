using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine.StepContracts;

/// <summary>A step of a pipeline. One class does one job. A step reads and writes the state of the run.</summary>
/// <typeparam name="TOptions">The type of the options that the <c>with</c> block of the step sets.</typeparam>
public interface IPipelineStep<in TOptions>
    where TOptions : class
{
    /// <summary>Runs the step.</summary>
    /// <param name="context">The run and the step.</param>
    /// <param name="options">The options, after the engine resolved the expressions.</param>
    /// <param name="ct">The token that cancels the step.</param>
    /// <returns>The outcome. An expected failure is an outcome, not an exception.</returns>
    ValueTask<StepOutcome> ExecuteAsync(StepContext context, TOptions options, CancellationToken ct);
}
