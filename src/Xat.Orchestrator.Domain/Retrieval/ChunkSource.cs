namespace Xat.Orchestrator.Domain.Retrieval;

/// <summary>Shows where a retrieved chunk came from.</summary>
/// <param name="Rag">The name of the RAG.</param>
/// <param name="DocId">The ID of the document.</param>
/// <param name="SourceKind">The kind of the source, for example <c>markdown</c> or <c>pdf</c>.</param>
/// <param name="Uri">The link to the source, or an empty text.</param>
/// <param name="Title">The title of the document.</param>
/// <param name="Version">The version of the document, or an empty text.</param>
public sealed record ChunkSource(string Rag, string DocId, string SourceKind, string Uri, string Title, string Version);
