namespace Xat.Orchestrator.Infrastructure.Options.Retrieval;

/// <summary>The retrieval aliases. A new context needs only a new entry here.</summary>
internal sealed class RetrievalOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Retrieval";

    /// <summary>Gets the aliases. The key is the alias that pipelines use.</summary>
    public Dictionary<string, RetrievalProfileOptions> Profiles { get; } = new(StringComparer.Ordinal);
}
