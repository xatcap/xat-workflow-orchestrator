using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

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
    IReadOnlySet<string> EarlierStepIds);
