namespace Xat.Orchestrator.Domain.Retrieval;

/// <summary>A ranked piece of a document that xat-rag returned.</summary>
/// <param name="ChunkId">The stable ID of the chunk.</param>
/// <param name="ParentId">The ID of the parent chunk, or <see langword="null"/>.</param>
/// <param name="Kind">The kind of the chunk, for example <c>section</c> or <c>comment</c>.</param>
/// <param name="Score">The rank score. A higher score is better. The score has no fixed range.</param>
/// <param name="Header">The heading path or the title. The text is untrusted.</param>
/// <param name="Text">The text of the chunk.</param>
/// <param name="Source">Where the chunk came from.</param>
public sealed record Chunk(
    string ChunkId,
    string? ParentId,
    string Kind,
    double Score,
    UntrustedText Header,
    UntrustedText Text,
    ChunkSource Source);
