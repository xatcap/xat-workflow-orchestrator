using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Api.Ingestion;

/// <summary>The address to which the ingestion proxy sends the requests. The port of xat-rag can change, so ask again after a failed connection.</summary>
internal interface IIngestionTarget
{
    /// <summary>Gets the current base address of xat-rag.</summary>
    /// <param name="ct">The token that cancels the lookup.</param>
    /// <returns>The base address, or an error when the address is not known.</returns>
    Task<Result<Uri>> LocateAsync(CancellationToken ct);

    /// <summary>Tells that a connection to the address failed, so that the next lookup reads the address again.</summary>
    void ReportFailure();
}
