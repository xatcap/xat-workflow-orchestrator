using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Steps.Flow;

/// <summary>The options of <see cref="FlowCompleteIfStep"/>.</summary>
public sealed class FlowCompleteIfOptions
{
    /// <summary>Gets the path of the value to test, for example <c>steps.retrieve.chunks</c> or <c>input.mode</c>.</summary>
    [Required]
    public string? Key { get; init; }

    /// <summary>Gets a flag. <see langword="true"/> ends the run when the value is empty. <see langword="false"/> ends it when the value is not empty.</summary>
    public bool? IsEmpty { get; init; }

    /// <summary>Gets a text. The run ends when the value, as text, is equal to this text.</summary>
    [JsonPropertyName("equals")]
    public string? EqualsValue { get; init; }

    /// <summary>Gets the text of the answer that the step returns when the run ends.</summary>
    [Required]
    public string? Text { get; init; }
}
