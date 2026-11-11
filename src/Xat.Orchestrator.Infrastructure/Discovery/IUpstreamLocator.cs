using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>Finds the current base address of an upstream service.</summary>
internal interface IUpstreamLocator
{
    /// <summary>Gets the base address of an upstream.</summary>
    /// <param name="upstream">The upstream.</param>
    /// <param name="ct">The token that cancels the lookup.</param>
    /// <returns>The base address, or an error when the address is not known.</returns>
    Task<Result<Uri>> LocateAsync(Upstream upstream, CancellationToken ct);

    /// <summary>Forgets the stored address, so that the next lookup reads the source again. Call it after a failed connection.</summary>
    /// <param name="upstream">The upstream.</param>
    void Invalidate(Upstream upstream);
}
