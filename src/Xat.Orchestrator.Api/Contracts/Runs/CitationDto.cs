using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Api.Contracts.Runs;

/// <summary>A cited source.</summary>
/// <param name="Number">The number of the source in the prompt.</param>
/// <param name="ChunkId">The ID of the chunk.</param>
/// <param name="Title">The title of the document.</param>
/// <param name="Uri">The link to the document, or an empty text.</param>
internal sealed record CitationDto(int Number, string ChunkId, string Title, string Uri)
{
    /// <summary>Makes the DTO from a citation.</summary>
    /// <param name="citation">The citation.</param>
    /// <returns>The DTO.</returns>
    public static CitationDto From(Citation citation) => new(citation.Number, citation.ChunkId, citation.Title, citation.Uri);
}
