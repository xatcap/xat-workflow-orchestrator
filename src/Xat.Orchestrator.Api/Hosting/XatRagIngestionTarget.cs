using Xat.Orchestrator.Api.Ingestion;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.XatRag;

namespace Xat.Orchestrator.Api.Hosting;

/// <summary>Connects the ingestion proxy to the address of xat-rag. This start code is the only Api code that uses the Infrastructure layer.</summary>
/// <param name="endpoint">The address of xat-rag.</param>
internal sealed class XatRagIngestionTarget(XatRagEndpoint endpoint) : IIngestionTarget
{
    /// <inheritdoc/>
    public Task<Result<Uri>> LocateAsync(CancellationToken ct) => endpoint.LocateAsync(ct);

    /// <inheritdoc/>
    public void ReportFailure() => endpoint.ReportFailure();
}
