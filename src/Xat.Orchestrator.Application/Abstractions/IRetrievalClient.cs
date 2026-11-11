using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>The port to xat-rag. The adapter hides the HTTP details and the error format of xat-rag.</summary>
public interface IRetrievalClient
{
    /// <summary>Gets the ranked chunks for a question. An empty result is a success.</summary>
    /// <param name="query">The question and the context.</param>
    /// <param name="ct">The token that cancels the call.</param>
    /// <returns>The chunks, or the error.</returns>
    Task<Result<RetrievedSet>> RetrieveAsync(RetrievalQuery query, CancellationToken ct);
}
