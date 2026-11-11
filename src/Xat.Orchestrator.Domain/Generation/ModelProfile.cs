namespace Xat.Orchestrator.Domain.Generation;

/// <summary>A configured alias for a model of xat-llm-engine, with its limits.</summary>
/// <param name="Alias">The name that pipelines use.</param>
/// <param name="Model">The model ID in the form <c>provider/model</c>.</param>
/// <param name="ContextWindow">The size of the context window in tokens.</param>
/// <param name="Tokenizer">The name of the tokenizer that counts tokens for this model.</param>
/// <param name="Parallel">The number of requests that the model runs at the same time.</param>
/// <param name="SupportsGrammar">Shows whether the model accepts a GBNF grammar.</param>
/// <param name="AllowedFields">The top-level request fields that the engine accepts for this model.</param>
public sealed record ModelProfile(
    string Alias,
    string Model,
    int ContextWindow,
    string Tokenizer,
    int Parallel,
    bool SupportsGrammar,
    IReadOnlyList<string> AllowedFields);
