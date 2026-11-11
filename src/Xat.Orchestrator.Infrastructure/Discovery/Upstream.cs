using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>The upstream services that the orchestrator calls.</summary>
internal enum Upstream
{
    /// <summary>The retrieval service xat-rag.</summary>
    XatRag,

    /// <summary>The LLM service xat-llm-engine.</summary>
    XatLlmEngine,
}
