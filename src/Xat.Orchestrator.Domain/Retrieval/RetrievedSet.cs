namespace Xat.Orchestrator.Domain.Retrieval;

/// <summary>The result of one retrieval.</summary>
/// <param name="Chunks">The chunks in rank order, best first. The list can be empty.</param>
/// <param name="OmittedCount">The number of candidates that xat-rag found but did not return.</param>
public sealed record RetrievedSet(IReadOnlyList<Chunk> Chunks, int OmittedCount);
