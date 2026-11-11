using System.ComponentModel.DataAnnotations;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Steps;

/// <summary>The options of <see cref="PromptAssembleStep"/>.</summary>
public sealed class PromptAssembleOptions
{
    /// <summary>Gets the alias of the model that sets the context window and the tokenizer. The default is the default alias.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the number of tokens that the step keeps free for the answer.</summary>
    [Range(0, 1_000_000)]
    public int ReserveOutput { get; init; } = 800;

    /// <summary>
    /// Gets the names of the sections in the order that the prompt uses them. A section that the list does not name goes after
    /// the named sections, in the order in which the steps added it. When the prompt is too large, the step removes chunks
    /// from the last chunk section first.
    /// </summary>
    public IReadOnlyList<string>? Order { get; init; }
}
