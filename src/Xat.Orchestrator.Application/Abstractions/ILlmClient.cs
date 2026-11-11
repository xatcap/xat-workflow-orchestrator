using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>The port to xat-llm-engine. The adapter hides the HTTP details and the error format of the engine.</summary>
public interface ILlmClient
{
    /// <summary>Gets one complete answer.</summary>
    /// <param name="request">The chat request.</param>
    /// <param name="ct">The token that cancels the call.</param>
    /// <returns>The answer, or the error.</returns>
    Task<Result<Completion>> CompleteAsync(LlmRequest request, CancellationToken ct);

    /// <summary>Gets the answer as a stream. A failure is the last item of the stream.</summary>
    /// <param name="request">The chat request.</param>
    /// <param name="ct">The token that cancels the call.</param>
    /// <returns>The parts of the answer, or an error as the last item.</returns>
    IAsyncEnumerable<Result<CompletionDelta>> StreamAsync(LlmRequest request, CancellationToken ct);

    /// <summary>Lists the model IDs that the engine offers.</summary>
    /// <param name="ct">The token that cancels the call.</param>
    /// <returns>The model IDs in the form <c>provider/model</c>, or the error.</returns>
    Task<Result<IReadOnlyList<string>>> ListModelsAsync(CancellationToken ct);
}
