using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Infrastructure.Options;

/// <summary>The place of the pipeline files.</summary>
internal sealed class PipelinesOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Pipelines";

    /// <summary>Gets the folder with the <c>*.yaml</c> pipeline files. A relative path starts at the folder of the application.</summary>
    public string Folder { get; init; } = "pipelines";
}
