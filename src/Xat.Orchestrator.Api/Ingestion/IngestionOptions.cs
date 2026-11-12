using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Api.Ingestion;

/// <summary>The settings of the ingestion proxy to xat-rag.</summary>
internal sealed class IngestionOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Ingestion";

    /// <summary>Gets a value that turns the whole proxy on or off. When it is off, every ingestion route gives 404.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets the names of the routes that the operator turns off, for example <c>delete-document</c> or <c>rebuild-rag</c>. See <see cref="IngestionRoutes"/>.</summary>
    public List<string> DisabledRoutes { get; } = [];

    /// <summary>Gets the highest size of an upload in bytes. xat-rag refuses an upload above 64 MiB.</summary>
    [Range(typeof(long), "1024", "1073741824", ErrorMessage = "must be from 1024 to 1073741824.")]
    public long MaxUploadBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Gets the time in seconds after which the proxy ends a request that sends or receives no data.</summary>
    [Range(1, 3600, ErrorMessage = "must be from 1 to 3600.")]
    public int ActivityTimeoutSeconds { get; init; } = 120;
}
