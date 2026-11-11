using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Steps;

/// <summary>The options of <see cref="PromptContextStep"/>.</summary>
public sealed partial class PromptContextOptions
{
    /// <summary>Gets the text that the caller sent as context. An empty text or a missing value adds no section.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the label of the section. Use the label in the <c>order</c> option of the assemble step.</summary>
    [RegularExpression("^[a-z0-9][a-z0-9-]{0,63}$", ErrorMessage = "The label must match [a-z0-9][a-z0-9-]{0,63}.")]
    public string Label { get; init; } = "caller-context";
}
