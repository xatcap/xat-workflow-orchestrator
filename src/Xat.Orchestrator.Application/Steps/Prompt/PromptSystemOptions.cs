namespace Xat.Orchestrator.Application.Steps.Prompt;

/// <summary>The options of <see cref="PromptSystemStep"/>.</summary>
public sealed class PromptSystemOptions
{
    /// <summary>Gets the system prompt text. An empty text or a missing value from an expression sets no system prompt.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the reference <c>name@version</c> of a template. Set this or <see cref="Text"/>.</summary>
    public string? Template { get; init; }
}
