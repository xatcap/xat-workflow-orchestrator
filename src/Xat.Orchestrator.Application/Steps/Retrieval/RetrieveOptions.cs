using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Application.Steps.Retrieval;

/// <summary>The options of <see cref="RetrieveStep"/>.</summary>
public sealed class RetrieveOptions
{
    /// <summary>Gets the alias of the retrieval profile. The alias points to a xat-rag context.</summary>
    [Required]
    public string? Profile { get; init; }

    /// <summary>Gets the highest number of chunks. The default is the value of the profile.</summary>
    [Range(1, 100)]
    public int? Chunks { get; init; }

    /// <summary>Gets the question. The default is the prompt of the caller.</summary>
    public string? Query { get; init; }
}
