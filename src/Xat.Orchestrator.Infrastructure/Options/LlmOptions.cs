using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Infrastructure.Options;

/// <summary>The model aliases. A new model needs only a new entry here.</summary>
internal sealed class LlmOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Llm";

    /// <summary>Gets the alias that steps use when they name no model.</summary>
    public string DefaultModel { get; init; } = "default";

    /// <summary>Gets the aliases. The key is the alias that pipelines use.</summary>
    public Dictionary<string, ModelOptions> Models { get; } = new(StringComparer.Ordinal);
}
