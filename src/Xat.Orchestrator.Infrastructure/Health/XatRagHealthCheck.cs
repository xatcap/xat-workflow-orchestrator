using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Xat.Orchestrator.Infrastructure.Health;

/// <summary>The health check of xat-rag.</summary>
internal sealed class XatRagHealthCheck(IHttpClientFactory factory)
    : UpstreamHealthCheck(factory, DependencyInjection.XatRagHealthClient, "xat-rag");
