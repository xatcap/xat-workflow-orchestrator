using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Infrastructure.Options.Retrieval;

/// <summary>One configured retrieval alias.</summary>
internal sealed class RetrievalProfileOptions
{
    /// <summary>Gets the name of the xat-rag context.</summary>
    public string Context { get; init; } = string.Empty;

    /// <summary>Gets the default number of chunks.</summary>
    [Range(1, 100, ErrorMessage = "must be from 1 to 100.")]
    public int Chunks { get; init; } = 8;
}
