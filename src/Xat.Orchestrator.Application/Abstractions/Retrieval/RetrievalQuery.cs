using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Abstractions.Retrieval;

/// <summary>A question for one xat-rag context.</summary>
/// <param name="Context">The name of the xat-rag context.</param>
/// <param name="Question">The question text.</param>
/// <param name="Chunks">The highest number of chunks to return.</param>
public sealed record RetrievalQuery(string Context, string Question, int Chunks);
