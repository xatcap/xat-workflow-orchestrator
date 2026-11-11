using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>A middleware around every step. A middleware can change the flow, for example with a time limit or a cache.</summary>
public interface IStepMiddleware
{
    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The run and the step.</param>
    /// <param name="nextStep">The next part of the chain. Call it to run the step.</param>
    /// <param name="ct">The token that cancels the step.</param>
    /// <returns>The outcome of the step, or an outcome that the middleware made.</returns>
    ValueTask<StepOutcome> InvokeAsync(StepContext context, StepInvocation nextStep, CancellationToken ct);
}
