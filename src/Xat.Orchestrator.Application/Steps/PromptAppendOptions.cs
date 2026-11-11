using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Steps;

/// <summary>The options of <see cref="PromptAppendStep"/>.</summary>
public sealed class PromptAppendOptions
{
    /// <summary>Gets the instruction text. Set this or <see cref="Template"/>.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the reference <c>name@version</c> of a template. Set this or <see cref="Text"/>.</summary>
    public string? Template { get; init; }

    /// <summary>Gets the name of the section. Use the name in the <c>order</c> option of the assemble step.</summary>
    [RegularExpression("^[a-z0-9][a-z0-9-]{0,63}$", ErrorMessage = "The name must match [a-z0-9][a-z0-9-]{0,63}.")]
    public string Name { get; init; } = "extra";
}
