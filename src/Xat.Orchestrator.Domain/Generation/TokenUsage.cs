namespace Xat.Orchestrator.Domain.Generation;

/// <summary>The number of tokens that an LLM call used.</summary>
/// <param name="PromptTokens">The tokens of the request.</param>
/// <param name="CompletionTokens">The tokens of the answer.</param>
public sealed record TokenUsage(int PromptTokens, int CompletionTokens)
{
    /// <summary>Gets a usage of zero tokens.</summary>
    public static TokenUsage None { get; } = new(0, 0);

    /// <summary>Adds another usage to this usage.</summary>
    /// <param name="other">The other usage.</param>
    /// <returns>A new usage with the sum of both.</returns>
    public TokenUsage Add(TokenUsage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new TokenUsage(PromptTokens + other.PromptTokens, CompletionTokens + other.CompletionTokens);
    }
}
