using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>Calls the next part of the chain. A middleware calls it to run the step.</summary>
/// <param name="ct">The token that cancels the step.</param>
/// <returns>The outcome of the step.</returns>
public delegate ValueTask<StepOutcome> StepInvocation(CancellationToken ct);
