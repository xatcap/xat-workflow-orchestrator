using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>The RAGs and contexts that xat-rag offers.</summary>
/// <param name="Contexts">The names of the contexts.</param>
/// <param name="Rags">The names of the RAGs.</param>
public sealed record RetrievalCatalog(IReadOnlyList<string> Contexts, IReadOnlyList<string> Rags);
