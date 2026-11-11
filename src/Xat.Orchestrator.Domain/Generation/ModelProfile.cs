namespace Xat.Orchestrator.Domain.Generation;

/// <summary>A configured alias for a model of xat-llm-engine.</summary>
/// <param name="Alias">The name that pipelines use.</param>
/// <param name="Model">The model ID in the form <c>provider/model</c>.</param>
/// <param name="Parallel">The number of requests that the model runs at the same time.</param>
/// <param name="AllowedFields">The top-level request fields that the engine accepts for this model.</param>
public sealed record ModelProfile(
    string Alias,
    string Model,
    int Parallel,
    IReadOnlyList<string> AllowedFields);
