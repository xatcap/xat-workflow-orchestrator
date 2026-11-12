using System.Text.Json;

namespace Xat.Orchestrator.Api.Contracts.Catalog;

/// <summary>The description of a pipeline for a caller.</summary>
/// <param name="Name">The name.</param>
/// <param name="Version">The version.</param>
/// <param name="Summary">The summary.</param>
/// <param name="Stream">Either <c>allowed</c>, <c>never</c> or <c>required</c>.</param>
/// <param name="InputSchema">The JSON Schema of the input.</param>
/// <param name="Steps">The step types that the pipeline uses, in order.</param>
internal sealed record PipelineDescriptorDto(
    string Name,
    int Version,
    string Summary,
    string Stream,
    JsonElement InputSchema,
    IReadOnlyList<string> Steps);
