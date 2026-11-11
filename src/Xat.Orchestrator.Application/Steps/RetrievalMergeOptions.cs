using System.ComponentModel.DataAnnotations;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Steps;

/// <summary>The options of <see cref="RetrievalMergeStep"/>.</summary>
public sealed class RetrievalMergeOptions
{
    /// <summary>Gets the IDs of the retrieval steps to merge.</summary>
    [Required]
    [MinLength(1)]
    public IReadOnlyList<string>? From { get; init; }

    /// <summary>Gets a value that shows whether the step removes chunks with the same ID. The default is <see langword="true"/>.</summary>
    public bool Dedupe { get; init; } = true;

    /// <summary>Gets the highest number of chunks in the result. The default is no limit.</summary>
    [Range(1, 1000)]
    public int? MaxChunks { get; init; }
}
