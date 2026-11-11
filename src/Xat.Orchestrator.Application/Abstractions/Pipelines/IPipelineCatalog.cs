using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Application.Abstractions.Pipelines;

/// <summary>The pipelines that the orchestrator can run.</summary>
public interface IPipelineCatalog
{
    /// <summary>Gets all pipelines.</summary>
    IReadOnlyCollection<PipelineDefinition> All { get; }

    /// <summary>Finds a pipeline.</summary>
    /// <param name="name">The name of the pipeline.</param>
    /// <returns>The pipeline, or a not-found error.</returns>
    Result<PipelineDefinition> Find(string name);
}
