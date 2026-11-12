using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Abstractions.Retrieval;

namespace Xat.Orchestrator.Application.Engine.StepContracts;

/// <summary>The data that a step can use to check its options when the service starts.</summary>
/// <param name="Profiles">The retrieval profiles.</param>
/// <param name="Models">The model profiles.</param>
/// <param name="Templates">The prompt templates.</param>
/// <param name="EarlierStepIds">The IDs of the steps that run before this step.</param>
public sealed record StepValidationContext(
    IRetrievalProfiles Profiles,
    IModelCatalog Models,
    IPromptTemplateStore Templates,
    IReadOnlySet<string> EarlierStepIds)
{
    /// <summary>
    /// Gets the JSON names of the options that hold one expression and that are not text, for example <c>isEmpty: "${input.flag}"</c>.
    /// Their value is known only when the run starts, so they are not set in the options that the check gets.
    /// </summary>
    public IReadOnlySet<string> DeferredOptions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
