using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Steps.Prompt;

/// <summary>The options of <see cref="PromptUserStep"/>.</summary>
public sealed class PromptUserOptions
{
    /// <summary>Gets the prompt text. This is usually the expression <c>${input.prompt}</c>. The step rejects an empty text with a clear error.</summary>
    public string? Text { get; init; }
}
