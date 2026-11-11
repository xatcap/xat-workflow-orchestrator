namespace Xat.Orchestrator.Domain.Retrieval;

/// <summary>A configured alias for a xat-rag context.</summary>
/// <param name="Alias">The name that pipelines and callers use.</param>
/// <param name="Context">The name of the xat-rag context.</param>
/// <param name="Chunks">The default number of chunks.</param>
public sealed record RetrievalProfile(string Alias, string Context, int Chunks);
