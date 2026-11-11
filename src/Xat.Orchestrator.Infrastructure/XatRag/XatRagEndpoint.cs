using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Discovery;

namespace Xat.Orchestrator.Infrastructure.XatRag;

/// <summary>Gives the address of xat-rag from the same locator that the retrieval adapter uses.</summary>
internal sealed class XatRagEndpoint(IUpstreamLocator locator) : IXatRagEndpoint
{
    /// <inheritdoc/>
    public Task<Result<Uri>> LocateAsync(CancellationToken ct) => locator.LocateAsync(Upstream.XatRag, ct);

    /// <inheritdoc/>
    public void ReportFailure() => locator.Invalidate(Upstream.XatRag);
}
