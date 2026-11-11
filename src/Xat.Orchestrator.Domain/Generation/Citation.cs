namespace Xat.Orchestrator.Domain.Generation;

/// <summary>A source that the answer cites. The prompt numbers the sources, and the model cites the numbers.</summary>
/// <param name="Number">The number of the source in the prompt.</param>
/// <param name="ChunkId">The ID of the cited chunk.</param>
/// <param name="Title">The title of the document.</param>
/// <param name="Uri">The link to the document, or an empty text.</param>
public sealed record Citation(int Number, string ChunkId, string Title, string Uri);
