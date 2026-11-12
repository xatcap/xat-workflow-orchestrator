using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Discovery;

namespace Xat.Orchestrator.Infrastructure.XatRag;

/// <summary>
/// Gives the address of xat-rag from the same locator that the retrieval adapter uses. The ingestion proxy of the host uses it,
/// because the proxy sends the HTTP request itself. Only the start code of the host can reach this class.
/// </summary>
public sealed class XatRagEndpoint
{
    private readonly IUpstreamLocator _locator;

    /// <summary>Initializes a new instance of the <see cref="XatRagEndpoint"/> class.</summary>
    /// <param name="locator">The locator of the upstream services.</param>
    internal XatRagEndpoint(IUpstreamLocator locator) => _locator = locator;

    /// <summary>Gets the current base address of xat-rag. The port of xat-rag can change at each start.</summary>
    /// <param name="ct">The token that cancels the lookup.</param>
    /// <returns>The base address, or an error when the address is not known.</returns>
    public Task<Result<Uri>> LocateAsync(CancellationToken ct) => _locator.LocateAsync(Upstream.XatRag, ct);

    /// <summary>Tells that a connection to the address failed, so that the next lookup reads the address again.</summary>
    public void ReportFailure() => _locator.Invalidate(Upstream.XatRag);
}
