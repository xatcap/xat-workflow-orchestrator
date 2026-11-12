namespace Xat.Orchestrator.Infrastructure.Health;

/// <summary>The health check of xat-llm-engine.</summary>
internal sealed class XatLlmEngineHealthCheck(IHttpClientFactory factory)
    : UpstreamHealthCheck(factory, DependencyInjection.XatLlmEngineHealthClient, "xat-llm-engine");
