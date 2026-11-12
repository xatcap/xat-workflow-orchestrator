using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Application.Abstractions.Pipelines;

/// <summary>The pipelines that the orchestrator can run.</summary>
public interface IPipelineCatalog
{
    /// <summary>Gets all pipelines.</summary>
    IReadOnlyCollection<PipelineDefinition> All { get; }

    /// <summary>Loads and checks all pipelines. The start calls it, so a mistake in a file stops the start.</summary>
    /// <returns>The problems of all files. An empty list means that all pipelines are valid.</returns>
    IReadOnlyList<string> Validate();

    /// <summary>Finds a pipeline.</summary>
    /// <param name="name">The name of the pipeline.</param>
    /// <returns>The pipeline, or a not-found error.</returns>
    Result<PipelineDefinition> Find(string name);
}
