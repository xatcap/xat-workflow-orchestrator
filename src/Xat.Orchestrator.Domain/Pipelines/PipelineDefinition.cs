using System.Text.Json;

namespace Xat.Orchestrator.Domain.Pipelines;

/// <summary>A named list of steps that the orchestrator runs for one request of a caller.</summary>
/// <param name="Name">The unique name of the pipeline.</param>
/// <param name="Version">The version of the pipeline definition.</param>
/// <param name="Summary">A short description for callers.</param>
/// <param name="Stream">Shows whether the pipeline can stream its answer.</param>
/// <param name="InputSchema">The JSON Schema of the input that a caller sends.</param>
/// <param name="Steps">The steps, in the order in which they run.</param>
public sealed record PipelineDefinition(
    string Name,
    int Version,
    string Summary,
    StreamMode Stream,
    JsonElement InputSchema,
    IReadOnlyList<StepDefinition> Steps);
