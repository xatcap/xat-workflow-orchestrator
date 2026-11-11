using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>
/// The address of the xat-rag service. The ingestion proxy uses it, because the proxy sends the HTTP request itself.
/// The port of xat-rag can change at each start, so a caller of this port must ask again after a failed connection.
/// </summary>
public interface IXatRagEndpoint
{
    /// <summary>Gets the current base address of xat-rag.</summary>
    /// <param name="ct">The token that cancels the lookup.</param>
    /// <returns>The base address, or an error when the address is not known.</returns>
    Task<Result<Uri>> LocateAsync(CancellationToken ct);

    /// <summary>Tells the port that a connection to the address failed, so that the next lookup reads the address again.</summary>
    void ReportFailure();
}
