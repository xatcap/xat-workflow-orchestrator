using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Infrastructure.Options;

/// <summary>One configured model alias.</summary>
internal sealed class ModelOptions
{
    /// <summary>Gets the model ID in the form <c>provider/model</c>.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the size of the context window in tokens.</summary>
    public int ContextWindow { get; init; } = 8192;

    /// <summary>Gets the name of the tokenizer. Only <c>estimate</c> is supported.</summary>
    public string Tokenizer { get; init; } = "estimate";

    /// <summary>Gets the number of requests that the model runs at the same time.</summary>
    public int Parallel { get; init; } = 1;

    /// <summary>Gets a value that shows whether the model accepts a GBNF grammar.</summary>
    public bool SupportsGrammar { get; init; }

    /// <summary>Gets the top-level request fields that the engine accepts for this model. The adapter sends no other field.</summary>
    public List<string> AllowedFields { get; } = [];
}
