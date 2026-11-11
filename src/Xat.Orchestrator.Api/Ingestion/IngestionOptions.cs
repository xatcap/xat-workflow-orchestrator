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
    public long MaxUploadBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Gets the time in seconds after which the proxy ends a request that sends or receives no data.</summary>
    public int ActivityTimeoutSeconds { get; init; } = 120;

    /// <summary>Checks the settings.</summary>
    /// <returns>The messages about wrong settings. An empty list means that the settings are valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (MaxUploadBytes is < 1024 or > 1024L * 1024 * 1024)
        {
            problems.Add($"{SectionName}:MaxUploadBytes must be from 1024 to 1073741824.");
        }

        if (ActivityTimeoutSeconds is < 1 or > 3600)
        {
            problems.Add($"{SectionName}:ActivityTimeoutSeconds must be from 1 to 3600.");
        }

        foreach (var name in DisabledRoutes.Where(name => !IngestionRoutes.Names.Contains(name, StringComparer.Ordinal)))
        {
            problems.Add($"{SectionName}:DisabledRoutes names the route '{name}', which does not exist. Known routes: {string.Join(", ", IngestionRoutes.Names)}.");
        }

        return problems;
    }
}
