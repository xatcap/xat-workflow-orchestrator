namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>The upstream services that the orchestrator calls.</summary>
internal enum Upstream
{
    /// <summary>The retrieval service xat-rag.</summary>
    XatRag,

    /// <summary>The LLM service xat-llm-engine.</summary>
    XatLlmEngine,
}
