using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Infrastructure.Options.Llm;

/// <summary>One configured model alias.</summary>
internal sealed class ModelOptions
{
    /// <summary>Gets the model ID in the form <c>provider/model</c>.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the number of requests that the model runs at the same time.</summary>
    public int Parallel { get; init; } = 1;

    /// <summary>Gets the top-level request fields that the engine accepts for this model. The adapter sends no other field.</summary>
    public List<string> AllowedFields { get; } = [];
}
