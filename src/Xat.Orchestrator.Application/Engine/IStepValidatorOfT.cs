using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>An optional interface of a step. The service calls it at start to find a wrong option early.</summary>
/// <typeparam name="TOptions">The type of the options.</typeparam>
public interface IStepValidator<in TOptions>
    where TOptions : class
{
    /// <summary>Checks the options of one use of the step.</summary>
    /// <param name="options">The options, with the expressions not yet resolved.</param>
    /// <param name="context">The data for the check.</param>
    /// <returns>The messages about wrong options. An empty list means that the options are valid.</returns>
    IReadOnlyList<string> Validate(TOptions options, StepValidationContext context);
}
