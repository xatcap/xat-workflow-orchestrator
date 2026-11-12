using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Application.Engine.Execution;

/// <summary>The run and the step that the engine executes now.</summary>
/// <param name="Run">The context of the run.</param>
/// <param name="Step">The definition of the step.</param>
public sealed record StepContext(PipelineContext Run, StepDefinition Step);
